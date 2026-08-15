using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace VllmChatClient.Test;

[Collection("Qwen3.8 local vLLM")]
public sealed class Qwen38LocalIntegrationTests(ITestOutputHelper output)
{
    private const string Endpoint = "http://localhost:8000/v1";
    private const string ModelId = "qwen3.8-27b-nvfp4";

    [Qwen38LocalFact]
    public async Task NonThinking_ReturnsAnswerWithoutReasoning()
    {
        using var client = CreateClient();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with exactly DIRECT_OK")],
            new VllmChatOptions
            {
                ThinkingEnabled = false,
                PreserveThinking = false,
                MaxOutputTokens = 64,
            });

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.True(string.IsNullOrWhiteSpace(reasoningResponse.Reason));
        Assert.Contains("DIRECT_OK", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Qwen38LocalTheory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("xhigh")]
    public async Task ThinkingEffort_ReturnsReasoningAndAnswer(string effort)
    {
        using var client = CreateClient();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Calculate 17 * 19. Give the final number after reasoning.")],
            CreateThinkingOptions(effort, maxOutputTokens: 512));

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.False(string.IsNullOrWhiteSpace(reasoningResponse.Reason));
        Assert.Contains("323", response.Text, StringComparison.Ordinal);
        output.WriteLine($"{effort}: reasoning={reasoningResponse.Reason.Length}, answer={response.Text.Length}");
    }

    [Qwen38LocalFact]
    public async Task Streaming_ReturnsReasoningAnswerAndUsage()
    {
        using var client = CreateClient();
        var reasoning = new System.Text.StringBuilder();
        var answer = new System.Text.StringBuilder();
        UsageDetails? usage = null;

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Calculate 12 * 13 and include the final number.")],
            CreateThinkingOptions("low", maxOutputTokens: 256)))
        {
            if (update is ReasoningChatResponseUpdate reasoningUpdate)
            {
                (reasoningUpdate.Thinking ? reasoning : answer).Append(reasoningUpdate.Text);
            }
            else if (update is UsageChatResponseUpdate usageUpdate)
            {
                usage = usageUpdate.Usage;
            }
            else
            {
                answer.Append(update.Text);
            }
        }

        Assert.NotEmpty(reasoning.ToString());
        Assert.Contains("156", answer.ToString(), StringComparison.Ordinal);
        Assert.NotNull(usage);
        Assert.True(usage!.InputTokenCount > 0);
        Assert.True(usage.OutputTokenCount > 0);
    }

    [Qwen38LocalFact]
    public async Task PreserveThinking_ReplaysVllmReasoningAsReasoningContent()
    {
        using var captureHandler = new CaptureForwardingHandler(new HttpClientHandler());
        using var httpClient = new HttpClient(captureHandler);
        using var client = CreateClient(httpClient);
        var options = CreateThinkingOptions("low", maxOutputTokens: 256);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Remember that the verification code is 7319, then confirm it."),
        };

        var firstResponse = await client.GetResponseAsync(messages, options);
        var firstReasoning = Assert.IsType<ReasoningChatResponse>(firstResponse).Reason;
        Assert.NotEmpty(firstReasoning);
        messages.Add(firstResponse.Messages[0]);
        messages.Add(new ChatMessage(ChatRole.User, "What verification code did I give you?"));

        var secondResponse = await client.GetResponseAsync(messages, options);

        Assert.Contains("7319", secondResponse.Text, StringComparison.Ordinal);
        Assert.Equal(2, captureHandler.RequestBodies.Count);
        using var requestJson = JsonDocument.Parse(captureHandler.RequestBodies[1]);
        var historicalAssistant = requestJson.RootElement.GetProperty("messages")[1];
        Assert.Equal(firstReasoning, historicalAssistant.GetProperty("reasoning_content").GetString());
        Assert.Null(captureHandler.AuthorizationSchemes[0]);
    }

    [Qwen38LocalFact]
    public async Task FunctionInvocation_ExecutesToolAndReturnsResult()
    {
        using var baseClient = CreateClient();
        var invocationCount = 0;
        var weatherTool = AIFunctionFactory.Create(
            (string city) =>
            {
                invocationCount++;
                return $"WEATHER_RESULT for {city}: sunny, 26C";
            },
            new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Gets the current weather for a city.",
            });
        using var client = new ChatClientBuilder(baseClient).UseFunctionInvocation().Build();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "You must call get_weather for Shanghai, then report the returned weather.")],
            CreateThinkingOptions("low", maxOutputTokens: 1024, tools: [weatherTool]));

        Assert.Equal(1, invocationCount);
        Assert.Contains("sunny", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Qwen38LocalFact]
    public async Task StreamingFunctionInvocation_ExecutesToolAndReturnsResult()
    {
        using var baseClient = CreateClient();
        var invocationCount = 0;
        var weatherTool = AIFunctionFactory.Create(
            (string city) =>
            {
                invocationCount++;
                return $"WEATHER_RESULT for {city}: rainy, 18C";
            },
            new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Gets the current weather for a city.",
            });
        using var client = new ChatClientBuilder(baseClient).UseFunctionInvocation().Build();
        var answer = new System.Text.StringBuilder();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "You must call get_weather for Beijing, then report the returned weather.")],
            CreateThinkingOptions("low", maxOutputTokens: 1024, tools: [weatherTool])))
        {
            if (update is not ReasoningChatResponseUpdate { Thinking: true })
            {
                answer.Append(update.Text);
            }
        }

        Assert.Equal(1, invocationCount);
        Assert.Contains("rain", answer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Qwen38LocalFact]
    public async Task JsonSchema_ReturnsSchemaConformingObject()
    {
        using var client = CreateClient();
        var options = new VllmChatOptions
        {
            ThinkingEnabled = false,
            PreserveThinking = false,
            MaxOutputTokens = 256,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                StructuredJsonSchemaTestHelper.CreateGreetingSchema(),
                "greeting",
                "A greeting payload"),
        };

        var response = await client.GetResponseAsync(
            StructuredJsonSchemaTestHelper.CreateGreetingMessages("菲菲"),
            options);

        StructuredJsonSchemaTestHelper.AssertGreetingJson(response.Text, "菲菲");
    }

    [Qwen38LocalFact]
    public async Task InlineImage_ReturnsVisualAnswer()
    {
        using var client = CreateClient();
        var imagePath = Path.Combine(AppContext.BaseDirectory, "test.jpg");
        Assert.True(File.Exists(imagePath), $"Missing test image: {imagePath}");
        var message = new ChatMessage(
            ChatRole.User,
            [
                new DataContent(await File.ReadAllBytesAsync(imagePath), "image/jpeg"),
                new TextContent("Briefly describe what is visible in this image."),
            ]);

        var response = await client.GetResponseAsync(
            [message],
            new VllmChatOptions
            {
                ThinkingEnabled = false,
                PreserveThinking = false,
                MaxOutputTokens = 128,
            });

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        output.WriteLine(response.Text);
    }

    [Qwen38LocalFact]
    public async Task InlineVideo_ReturnsVisualAnswer()
    {
        var ffmpegPath = FindExecutable("ffmpeg");
        if (ffmpegPath is null)
        {
            throw Xunit.Sdk.SkipException.ForSkip("ffmpeg is required to generate the local video fixture.");
        }

        var videoPath = Path.Combine(Path.GetTempPath(), $"qwen38-{Guid.NewGuid():N}.mp4");
        try
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                ArgumentList =
                {
                    "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
                    "color=c=red:s=64x64:d=1", "-pix_fmt", "yuv420p", "-y", videoPath,
                },
                UseShellExecute = false,
                CreateNoWindow = true,
            }))
            {
                Assert.NotNull(process);
                await process!.WaitForExitAsync();
                Assert.Equal(0, process.ExitCode);
            }

            using var client = CreateClient();
            var message = new ChatMessage(
                ChatRole.User,
                [
                    new DataContent(await File.ReadAllBytesAsync(videoPath), "video/mp4"),
                    new TextContent("What is the dominant color in this video? Reply briefly."),
                ]);
            var response = await client.GetResponseAsync(
                [message],
                new VllmChatOptions
                {
                    ThinkingEnabled = false,
                    PreserveThinking = false,
                    MaxOutputTokens = 128,
                });

            Assert.Contains("red", response.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
        }
    }

    private static VllmQwen3NextChatClient CreateClient(HttpClient? httpClient = null)
        => new(Endpoint, token: null, ModelId, httpClient);

    private static VllmChatOptions CreateThinkingOptions(
        string effort,
        int maxOutputTokens,
        IList<AITool>? tools = null)
        => new()
        {
            ThinkingEnabled = true,
            PreserveThinking = true,
            ReasoningEffort = effort,
            MaxOutputTokens = maxOutputTokens,
            Tools = tools,
        };

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), OperatingSystem.IsWindows() ? $"{name}.exe" : name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class CaptureForwardingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        public List<string> RequestBodies { get; } = [];
        public List<string?> AuthorizationSchemes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}

[CollectionDefinition("Qwen3.8 local vLLM", DisableParallelization = true)]
public sealed class Qwen38LocalIntegrationCollection;

public sealed class Qwen38LocalFactAttribute : FactAttribute
{
    public Qwen38LocalFactAttribute()
    {
        if (!Qwen38LocalTestGate.IsEnabled)
        {
            Skip = Qwen38LocalTestGate.SkipReason;
        }
    }
}

public sealed class Qwen38LocalTheoryAttribute : TheoryAttribute
{
    public Qwen38LocalTheoryAttribute()
    {
        if (!Qwen38LocalTestGate.IsEnabled)
        {
            Skip = Qwen38LocalTestGate.SkipReason;
        }
    }
}

internal static class Qwen38LocalTestGate
{
    internal const string SkipReason =
        "Set RUN_QWEN38_LOCAL_TESTS=1 to run tests against the local Qwen3.8 vLLM server.";

    internal static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable("RUN_QWEN38_LOCAL_TESTS"),
        "1",
        StringComparison.Ordinal);
}
