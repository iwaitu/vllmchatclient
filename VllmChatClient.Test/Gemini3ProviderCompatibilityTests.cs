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
