using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace VllmChatClient.Test;

public class Gemini3FunctionCallTest
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta";
    private const string ModelId = "gemini-3.5-flash";
    private readonly ITestOutputHelper _output;

    public Gemini3FunctionCallTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private string? GetApiKeyOrSkip()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
            return null;
        }

        return apiKey;
    }

    private static VllmGemini3ChatClient CreateClient(string apiKey)
        => new(Endpoint, apiKey, ModelId);

    private async Task<ChatResponse> GetResponseWithRetryAsync(
        IChatClient client,
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientExternalApiFailure(ex))
            {
                _output.WriteLine($"Transient Gemini request failure on attempt {attempt}: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    private async Task<string> GetStreamingTextWithRetryAsync(
        IChatClient client,
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var text = string.Empty;
                await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    foreach (var content in update.Contents.OfType<TextContent>())
                    {
                        text += content.Text;
                    }
                }

                return text;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientExternalApiFailure(ex))
            {
                _output.WriteLine($"Transient Gemini streaming failure on attempt {attempt}: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransientExternalApiFailure(Exception ex)
        => ex is TaskCanceledException
           || ex is HttpRequestException
           {
               StatusCode: null
                         or HttpStatusCode.RequestTimeout
                         or HttpStatusCode.TooManyRequests
                         or HttpStatusCode.InternalServerError
                         or HttpStatusCode.BadGateway
                         or HttpStatusCode.ServiceUnavailable
                         or HttpStatusCode.GatewayTimeout
           };

    [Description("Get deterministic weather for a city.")]
    private static string GetWeatherForGemini([Description("City name, for example Paris or Tokyo.")] string city)
        => city switch
        {
            "Paris" or "巴黎" => "Paris weather: sunny, 18C.",
            "Tokyo" or "东京" => "Tokyo weather: cloudy, 21C.",
            _ => $"{city} weather: unavailable."
        };

    [Fact]
    public async Task Gemini35Flash_ChatSmokeTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Reply with exactly: pong")
        };

        var options = new GeminiChatOptions
        {
            Temperature = 0
        };

        var response = await GetResponseWithRetryAsync(client, messages, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);
        Assert.Contains("pong", response.Text, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Model: {response.ModelId}");
        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_ChatTest_WithSystemMessage()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个智能助手，名字叫菲菲。回答必须简短。"),
            new(ChatRole.User, "你是谁？")
        };

        var response = await GetResponseWithRetryAsync(client, messages, new GeminiChatOptions { Temperature = 0 });

        Assert.NotNull(response);
        Assert.Single(response.Messages);
        Assert.Contains("菲菲", response.Text);
        Assert.Equal(ModelId, response.ModelId);

        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_MultiTurnChatTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请记住暗号 blue-42。只回复 OK。")
        };

        var firstResponse = await GetResponseWithRetryAsync(client, messages, new GeminiChatOptions { Temperature = 0 });
        Assert.NotNull(firstResponse);
        Assert.NotEmpty(firstResponse.Text);

        messages.Add(firstResponse.Messages[0]);
        messages.Add(new ChatMessage(ChatRole.User, "刚才的暗号是什么？只输出暗号。"));

        var secondResponse = await GetResponseWithRetryAsync(client, messages, new GeminiChatOptions { Temperature = 0 });

        Assert.NotNull(secondResponse);
        Assert.Contains("blue-42", secondResponse.Text, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"First response: {firstResponse.Text}");
        _output.WriteLine($"Second response: {secondResponse.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_JsonOutputTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个智能助手，名字叫菲菲。"),
            new(ChatRole.User, "请仅输出一个 JSON 对象，包含 name 和 greeting 两个字符串字段。不要输出 markdown 或代码块。")
        };

        var response = await GetResponseWithRetryAsync(client, messages, new GeminiChatOptions { Temperature = 0 });

        Assert.NotNull(response);
        Assert.Single(response.Messages);
        Assert.DoesNotContain("```", response.Text);

        var jsonText = TryExtractFirstJsonValue(response.Text);
        Assert.False(string.IsNullOrWhiteSpace(jsonText), $"未找到 JSON 片段: '{response.Text}'");

        using var json = JsonDocument.Parse(jsonText!);
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.True(json.RootElement.TryGetProperty("name", out _), $"Missing name property: '{jsonText}'");
        Assert.True(json.RootElement.TryGetProperty("greeting", out _), $"Missing greeting property: '{jsonText}'");

        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_JsonSchemaOutputTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var options = new GeminiChatOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                StructuredJsonSchemaTestHelper.CreateGreetingSchema(),
                "greeting_payload",
                "Greeting payload")
        };

        var response = await GetResponseWithRetryAsync(client, StructuredJsonSchemaTestHelper.CreateGreetingMessages(), options);

        Assert.NotNull(response);
        Assert.Single(response.Messages);
        StructuredJsonSchemaTestHelper.AssertGreetingJson(response.Text);

        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_StreamChatTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Reply with exactly: stream-pong")
        };

        var text = await GetStreamingTextWithRetryAsync(client, messages, new GeminiChatOptions { Temperature = 0 });

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("stream-pong", text, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Stream response: {text}");
    }

    [Fact]
    public async Task Gemini35Flash_ManualFunctionCallTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var client = CreateClient(apiKey);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "You must call GetWeatherForGemini with city Paris, then answer using the tool result.")
        };

        var options = new GeminiChatOptions
        {
            Temperature = 0,
            ReasoningLevel = GeminiReasoningLevel.Low,
            Tools = [AIFunctionFactory.Create(GetWeatherForGemini)]
        };

        var response = await GetResponseWithRetryAsync(client, messages, options);
        Assert.NotNull(response);
        Assert.Single(response.Messages);

        var functionCalls = response.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
        Assert.NotEmpty(functionCalls);

        var functionCall = Assert.Single(functionCalls);
        Assert.Equal(nameof(GetWeatherForGemini), functionCall.Name);
        Assert.NotNull(functionCall.Arguments);

        var city = functionCall.Arguments!.TryGetValue("city", out var cityValue)
            ? cityValue?.ToString() ?? string.Empty
            : string.Empty;
        Assert.Contains("Paris", city, StringComparison.OrdinalIgnoreCase);

        var toolResult = GetWeatherForGemini(city);
        messages.Add(response.Messages[0]);
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(functionCall.CallId, toolResult)]));

        var finalResponse = await GetResponseWithRetryAsync(client, messages, options);

        Assert.NotNull(finalResponse);
        Assert.Single(finalResponse.Messages);
        Assert.Contains("18", finalResponse.Text);

        _output.WriteLine($"Function call: {functionCall.Name}({JsonSerializer.Serialize(functionCall.Arguments)})");
        _output.WriteLine($"Final response: {finalResponse.Text}");
    }

    [Fact]
    public async Task Gemini35Flash_AutomaticFunctionInvocationTest()
    {
        var apiKey = GetApiKeyOrSkip();
        if (apiKey is null)
        {
            return;
        }

        using var innerClient = CreateClient(apiKey);
        using var client = new ChatClientBuilder(innerClient)
            .UseFunctionInvocation()
            .Build();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Use the weather tool to check Paris weather, then tell me the temperature.")
        };

        var options = new GeminiChatOptions
        {
            Temperature = 0,
            ReasoningLevel = GeminiReasoningLevel.Low,
            Tools = [AIFunctionFactory.Create(GetWeatherForGemini)]
        };

        var response = await GetResponseWithRetryAsync(client, messages, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Messages);
        Assert.Contains("18", response.Text);

        _output.WriteLine($"Response: {response.Text}");
    }

    private static string? TryExtractFirstJsonValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();

        try
        {
            using var _ = JsonDocument.Parse(text);
            return text;
        }
        catch (JsonException)
        {
        }

        var match = Regex.Match(text, @"(\{.*\}|\[.*\])", RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(match.Value);
            return match.Value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
