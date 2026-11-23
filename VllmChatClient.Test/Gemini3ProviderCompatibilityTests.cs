using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class Gemini3ProviderCompatibilityTests
{
    [Fact]
    public void Constructor_WithGeminiNativeEndpoint_UsesXGoogApiKeyHeader()
    {
        using var httpClient = new HttpClient(new CaptureHandler("{}"));
        _ = new VllmGemini3ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemini-3-pro-preview",
            httpClient);

        Assert.True(httpClient.DefaultRequestHeaders.Contains("x-goog-api-key"));
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task GeminiNative_Request_UsesResponseJsonSchema_ForStructuredOutput()
    {
        const string responseJson = """
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [
          {
            "text": "{\"name\":\"菲菲\",\"greeting\":\"你好\"}"
          }
        ]
      },
      "finishReason": "STOP",
      "index": 0
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemini3ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemini-3-pro-preview",
            httpClient);

        _ = await client.GetResponseAsync(
            StructuredJsonSchemaTestHelper.CreateGreetingMessages(),
            new GeminiChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    StructuredJsonSchemaTestHelper.CreateGreetingSchema(),
                    "greeting_payload",
                    "Greeting payload")
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.True(generationConfig.TryGetProperty("responseJsonSchema", out var responseJsonSchema));
        Assert.False(generationConfig.TryGetProperty("responseSchema", out _));
        Assert.Equal(JsonValueKind.Object, responseJsonSchema.ValueKind);
        Assert.False(responseJsonSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("object", responseJsonSchema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GeminiNative_Request_PreservesFunctionParameterJsonSchema()
    {
        using var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "args": {
                  "type": "object",
                  "additionalProperties": { "type": "string" }
                }
              },
              "additionalProperties": false,
              "required": ["args"]
            }
            """);
        using var emptySchema = JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        const string response = """{"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""";
        ChatMessage[] messages = [
            new(ChatRole.User, "Reply briefly."),
            new(ChatRole.Assistant, [new FunctionCallContent("with_arguments", "with_arguments")]),
            new(ChatRole.Tool, [new FunctionResultContent("with_arguments", "probe result")])
        ];
        var options = new ChatOptions
        {
            Tools = [
                new SchemaFunction("with_arguments", schema.RootElement),
                new SchemaFunction("without_arguments", emptySchema.RootElement),
                new SchemaFunction("without_schema")
            ]
        };

        var handler = new CaptureHandler(response);
        using var httpClient = new HttpClient(handler);
        using var client = new VllmGemini3ChatClient(
            "https://generativelanguage.googleapis.com/v1beta", "gemini-key", "gemini-4-hydrogen", httpClient);
        await client.GetResponseAsync(messages, options);
        AssertDeclarations(handler.LastRequestBody!);

        var streamHandler = new CaptureHandler($"data: {response}\n\n");
        using var streamHttpClient = new HttpClient(streamHandler);
        using var streamClient = new VllmGemini3ChatClient(
            "https://generativelanguage.googleapis.com/v1beta", "gemini-key", "gemini-4-hydrogen", streamHttpClient);
        await foreach (var _ in streamClient.GetStreamingResponseAsync(messages, options)) { }
        AssertDeclarations(streamHandler.LastRequestBody!);

        void AssertDeclarations(string requestBody)
        {
            using var wire = JsonDocument.Parse(requestBody);
            var declarations = wire.RootElement.GetProperty("tools")[0].GetProperty("functionDeclarations");
            Assert.Equal(3, declarations.GetArrayLength());
            Assert.All(declarations.EnumerateArray(), declaration => Assert.False(declaration.TryGetProperty("parameters", out _)));
            Assert.True(JsonElement.DeepEquals(schema.RootElement, declarations[0].GetProperty("parametersJsonSchema")));
            Assert.True(JsonElement.DeepEquals(emptySchema.RootElement, declarations[1].GetProperty("parametersJsonSchema")));
            Assert.False(declarations[2].TryGetProperty("parametersJsonSchema", out _));
            var toolReply = wire.RootElement.GetProperty("contents")[2];
            Assert.Equal("user", toolReply.GetProperty("role").GetString());
            var functionResponse = toolReply.GetProperty("parts")[0].GetProperty("functionResponse");
            Assert.Equal("with_arguments", functionResponse.GetProperty("name").GetString());
            Assert.Equal("probe result", functionResponse.GetProperty("response").GetProperty("result").GetString());
        }
    }

    [Fact]
    public async Task OpenRouter_Request_UsesBearerAndReasoningEnabled()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemini-3.1-pro-preview",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "4"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemini3ChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            "google/gemini-3.1-pro-preview",
            httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "How many r's are in strawberry?")
        };

        var options = new GeminiChatOptions
        {
            ReasoningLevel = GeminiReasoningLevel.Low
        };

        _ = await client.GetResponseAsync(messages, options);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequestUri!.ToString());

        Assert.NotNull(httpClient.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", httpClient.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("openrouter-key", httpClient.DefaultRequestHeaders.Authorization.Parameter);
        Assert.False(httpClient.DefaultRequestHeaders.Contains("x-goog-api-key"));

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("enabled", out var enabled));
        Assert.True(enabled.GetBoolean());
    }

    private sealed class SchemaFunction(string name, JsonElement schema = default) : AIFunction
    {
        public override string Name => name;
        public override string Description => "Return a test result.";
        public override JsonElement JsonSchema => schema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(null);
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
