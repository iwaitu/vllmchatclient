using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// api.openai.com answers 400 unknown_parameter to the OpenRouter/vLLM-style extensions (nested "options", "reasoning",
/// "include_reasoning", ...). For that host the request must carry only fields the official API defines; other hosts keep
/// the existing shape.
/// </summary>
public class OfficialOpenAiRequestTests
{
    private static readonly string[] Foreign = ["options", "reasoning", "include_reasoning", "enable_reasoning", "stream_reasoning", "extra_body", "format"];

    [Theory]
    [InlineData("https://api.openai.com/v1/{1}", VllmApiMode.ChatCompletions)]
    [InlineData("https://api.openai.com/v1/chat/completions", VllmApiMode.ChatCompletions)]
    [InlineData("https://api.openai.com/v1/{1}", VllmApiMode.Responses)]
    public async Task OfficialHost_SendsOnlyOfficialFields(string endpoint, VllmApiMode mode)
    {
        var root = await CaptureAsync(endpoint, mode, new ChatOptions { Temperature = 0.6f, TopP = 0.9f, MaxOutputTokens = 321 });

        foreach (var name in Foreign)
        {
            Assert.False(root.TryGetProperty(name, out _), $"unexpected '{name}' in the request");
        }

        Assert.Equal(0.6, root.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal(0.9, root.GetProperty("top_p").GetDouble(), 3);
        Assert.Equal(321, root.GetProperty(mode == VllmApiMode.Responses ? "max_output_tokens" : "max_completion_tokens").GetInt32());
        Assert.False(root.TryGetProperty("max_tokens", out _));
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    public async Task OfficialHost_ThinkingFlagAloneAddsNothing(VllmApiMode mode)
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", mode, new VllmChatOptions { ThinkingEnabled = true });

        Assert.False(root.TryGetProperty("reasoning", out _));
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.False(root.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task OfficialHost_ReasoningEffort_GoesTopLevelForChatCompletions()
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.ChatCompletions, new VllmChatOptions { ReasoningEffort = "low" });

        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public async Task OfficialHost_ReasoningEffort_UsesReasoningObjectForResponses()
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.Responses, new OpenAiGptChatOptions { ReasoningLevel = OpenAiGptReasoningLevel.High });

        Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    // The GPT-5/6 families refuse temperature/top_p while reasoning, and refuse function tools on /chat/completions unless the
    // reasoning effort is "none" (both answers observed from api.openai.com for gpt-6.1-terra).
    [Fact]
    public async Task ReasoningModel_DropsSampling_WhenItReasons()
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.Responses, new VllmChatOptions { Temperature = 0.6f, TopP = 0.9f, ThinkingEnabled = true }, "gpt-6.1-terra");

        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("top_p", out _));
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    public async Task ReasoningModel_ThinkingDisabled_SendsEffortNone_AndKeepsSampling(VllmApiMode mode)
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", mode, new VllmChatOptions { Temperature = 0.6f, ThinkingEnabled = false }, "gpt-6.1-terra");

        var effort = mode == VllmApiMode.Responses
            ? root.GetProperty("reasoning").GetProperty("effort").GetString()
            : root.GetProperty("reasoning_effort").GetString();
        Assert.Equal("none", effort);
        Assert.Equal(0.6, root.GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task ReasoningModel_WithToolsOnChatCompletions_DefaultsToEffortNone()
    {
        var tool = AIFunctionFactory.Create((string city) => city, new AIFunctionFactoryOptions { Name = "get_weather", Description = "Get weather." });
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.ChatCompletions, new ChatOptions { Tools = [tool] }, "gpt-6.1-terra");

        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
    }

    [Theory]
    [InlineData("gpt-5.5-pro")]
    [InlineData("o3")]
    public async Task ModelsWithoutEffortNone_NeverGetIt(string model)
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.ChatCompletions, new VllmChatOptions { ThinkingEnabled = false }, model);

        Assert.False(root.TryGetProperty("reasoning_effort", out _));
    }

    // Structured output: Chat Completions keeps response_format; Responses moves it to text.format. Neither may carry the vLLM-only
    // structured_outputs (under extra_body) or the Ollama-style "format".
    [Fact]
    public async Task OfficialHost_JsonSchema_ChatCompletions_KeepsResponseFormat_WithoutVllmExtras()
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.ChatCompletions,
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"}},"required":["a"],"additionalProperties":false}""").RootElement, "answer") });

        Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("answer", root.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("extra_body", out _));
        Assert.False(root.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task OfficialHost_JsonSchema_Responses_UsesTextFormat()
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", VllmApiMode.Responses,
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"}},"required":["a"],"additionalProperties":false}""").RootElement, "answer") });

        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("extra_body", out _));
        Assert.False(root.TryGetProperty("structured_outputs", out _));
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("answer", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("object", format.GetProperty("schema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    public async Task OfficialHost_JsonObject_UsesTheModeOwnShape(VllmApiMode mode)
    {
        var root = await CaptureAsync("https://api.openai.com/v1/{1}", mode, new ChatOptions { ResponseFormat = ChatResponseFormat.Json });

        if (mode == VllmApiMode.Responses)
        {
            Assert.False(root.TryGetProperty("response_format", out _));
            Assert.Equal("json_object", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        }
        else
        {
            Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        }

        Assert.False(root.TryGetProperty("extra_body", out _));
    }

    [Fact]
    public async Task OtherHosts_KeepTheirExistingShape()
    {
        var root = await CaptureAsync("https://openrouter.ai/api/v1/{1}", VllmApiMode.ChatCompletions, new ChatOptions { Temperature = 0.6f });

        Assert.True(root.TryGetProperty("options", out var options));
        Assert.Equal(0.6, options.GetProperty("temperature").GetDouble(), 3);
        Assert.True(options.TryGetProperty("extra_body", out _));
    }

    [Fact]
    public async Task OtherHosts_Responses_SendNoBooleanReasoningExtras()
    {
        // The Responses request flattens extra_body to the top level, where "reasoning" must be an object:
        // OpenRouter answers 400 "expected object, received boolean" to the flags Chat Completions carries.
        var root = await CaptureAsync("https://openrouter.ai/api/v1/{1}", VllmApiMode.Responses, new ChatOptions { MaxOutputTokens = 64 }, "openai/gpt-5-nano");

        foreach (var name in new[] { "reasoning", "include_reasoning", "enable_reasoning", "stream_reasoning" })
        {
            Assert.False(root.TryGetProperty(name, out _), $"unexpected '{name}' in the Responses request");
        }
        Assert.Equal("openai/gpt-5-nano", root.GetProperty("model").GetString());
    }

    [Fact]
    public async Task OtherHosts_Responses_ThinkingEnabled_SendsReasoningAsAnObject()
    {
        var root = await CaptureAsync("https://openrouter.ai/api/v1/{1}", VllmApiMode.Responses, new VllmChatOptions { ThinkingEnabled = true }, "openai/gpt-5-nano");

        Assert.Equal(JsonValueKind.Object, root.GetProperty("reasoning").ValueKind);
        Assert.False(root.TryGetProperty("include_reasoning", out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OtherHosts_Responses_StructuredOutput_UsesTextFormat(bool schema)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""");
        var format = schema ? ChatResponseFormat.ForJsonSchema(document.RootElement, "result") : ChatResponseFormat.Json;
        var root = await CaptureAsync("https://openrouter.ai/api/v1/{1}", VllmApiMode.Responses, new ChatOptions { ResponseFormat = format }, "openai/gpt-5-nano");

        Assert.Equal(schema ? "json_schema" : "json_object", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("structured_outputs", out _));
    }

    private static async Task<JsonElement> CaptureAsync(string endpoint, VllmApiMode mode, ChatOptions options, string model = "gpt-4.1")
    {
        string? body = null;
        using var http = new HttpClient(new CaptureHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Reply(mode), Encoding.UTF8, "application/json")
            };
        }));
        var client = new VllmOpenAiGptClient(endpoint, "test-key", model, http, mode);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        Assert.NotNull(body);
        return JsonDocument.Parse(body!).RootElement.Clone();
    }

    private static string Reply(VllmApiMode mode) => mode == VllmApiMode.Responses
        ? """{"id":"r","object":"response","status":"completed","model":"m","output":[{"id":"m","type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}"""
        : """{"id":"c","model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
