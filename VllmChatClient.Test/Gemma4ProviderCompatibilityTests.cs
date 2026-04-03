using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class Gemma4ProviderCompatibilityTests
{
    [Fact]
    public void Constructor_WithGoogleNativeEndpoint_UsesXGoogApiKeyHeader()
    {
        using var httpClient = new HttpClient(new CaptureHandler("{}"));
        _ = new VllmGemma4ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemma-4-31b-it",
            httpClient);

        Assert.True(httpClient.DefaultRequestHeaders.Contains("x-goog-api-key"));
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task GoogleNative_Request_UsesContentsAndGenerationConfig()
    {
        const string responseJson = """
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [
          {
            "text": "Hello!"
          }
        ]
      },
      "finishReason": "STOP",
      "index": 0
    }
  ],
  "usageMetadata": {
    "promptTokenCount": 1,
    "candidatesTokenCount": 1,
    "totalTokenCount": 2
  }
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemma-4-31b-it",
            httpClient);

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new VllmChatOptions
            {
                ThinkingEnabled = true,
                Temperature = 0.7f,
                TopP = 0.9f,
                MaxOutputTokens = 256
            });

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent", handler.LastRequestUri?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("contents", out var contents));
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal("hi", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());

        Assert.True(doc.RootElement.TryGetProperty("generationConfig", out var generationConfig));
        Assert.Equal("HIGH", generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.Equal(0.7f, generationConfig.GetProperty("temperature").GetSingle());
        Assert.Equal(0.9f, generationConfig.GetProperty("topP").GetSingle());
        Assert.Equal(256, generationConfig.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task GoogleNative_Response_SeparatesThoughtFromAnswer()
    {
        const string responseJson = """
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [
          {
            "text": "Internal reasoning.",
            "thought": true
          },
          {
            "text": "Hello! How can I help you today?"
          }
        ]
      },
      "finishReason": "STOP",
      "index": 0
    }
  ],
  "usageMetadata": {
    "promptTokenCount": 1,
    "candidatesTokenCount": 2,
    "totalTokenCount": 3
  }
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemma-4-31b-it",
            httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.Equal("Internal reasoning.", reasoningResponse.Reason);
        Assert.Equal("Hello! How can I help you today?", response.Text);
        Assert.DoesNotContain("Internal reasoning.", response.Text);
    }

    [Fact]
    public async Task GoogleNative_Response_HidesThoughtWhenThinkingDisabled()
    {
        const string responseJson = """
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [
          {
            "text": "Internal reasoning.",
            "thought": true
          },
          {
            "text": "Hello!"
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
        var client = new VllmGemma4ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemma-4-31b-it",
            httpClient);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new VllmChatOptions { ThinkingEnabled = false });

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.Equal(string.Empty, reasoningResponse.Reason);
        Assert.Equal("Hello!", response.Text);
    }

    [Fact]
    public async Task GoogleNative_Streaming_SeparatesThoughtFromAnswer()
    {
        const string streamResponse = """
data: {"candidates":[{"content":{"role":"model","parts":[{"text":"Internal reasoning.","thought":true}]},"finishReason":null,"index":0}]}

data: {"candidates":[{"content":{"role":"model","parts":[{"text":"Hello!"}]},"finishReason":"STOP","index":0}]}

""";

        var handler = new CaptureHandler(streamResponse, "text/event-stream");
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-key",
            "gemma-4-31b-it",
            httpClient);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, static update => update is ReasoningChatResponseUpdate { Thinking: true } reasoning && reasoning.Text == "Internal reasoning.");
        Assert.Contains(updates, static update => update.Text == "Hello!");
        Assert.DoesNotContain(updates, static update => update is ReasoningChatResponseUpdate { Thinking: false } reasoning && reasoning.Text == "Internal reasoning.");
    }

    [Fact]
    public async Task OpenRouter_Request_UsesBearerAndEnablesReasoningByDefault()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "3"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            httpClient: httpClient);

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "How many r's are in strawberry?")]);

        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequestUri?.ToString());
        Assert.NotNull(httpClient.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", httpClient.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("openrouter-key", httpClient.DefaultRequestHeaders.Authorization.Parameter);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("gemma-4-31b-it", doc.RootElement.GetProperty("model").GetString());
        Assert.True(doc.RootElement.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("enabled", out var enabled));
        Assert.True(enabled.GetBoolean());
    }

    [Fact]
    public async Task OpenRouter_Request_UsesReasoningToggleFromVllmOptions()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "3"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            httpClient: httpClient);

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "How many r's are in strawberry?")],
            new VllmChatOptions
            {
                ThinkingEnabled = false
            });

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("enabled", out var enabled));
        Assert.False(enabled.GetBoolean());
    }

    [Fact]
    public async Task PlaceholderEndpoint_Request_FormatsSingleChatCompletionsPath()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "gemma4",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Hello!"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "http://localhost:8000/v1/{1}",
            "test-key",
            "gemma4",
            httpClient);

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("http://localhost:8000/v1/chat/completions", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OpenRouter_Response_HidesReasoningWhenThinkingDisabled()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "3",
        "reasoning_content": "Count the letters carefully."
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            httpClient: httpClient);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "How many r's are in strawberry?")],
            new VllmChatOptions
            {
                ThinkingEnabled = false
            });

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.Equal(string.Empty, reasoningResponse.Reason);
        Assert.Equal("3", response.Text);
    }

    [Fact]
    public async Task OpenRouter_Streaming_HidesReasoningUpdatesWhenThinkingDisabled()
    {
        const string streamResponse = """
data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1771436118,"model":"google/gemma-4-31b-it","choices":[{"index":0,"delta":{"role":"assistant","reasoning":"Count the letters carefully. "},"finish_reason":null}]}

data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1771436118,"model":"google/gemma-4-31b-it","choices":[{"index":0,"delta":{"content":"3"},"finish_reason":"stop"}]}

data: [DONE]

""";

        var handler = new CaptureHandler(streamResponse, "text/event-stream");
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            httpClient: httpClient);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "How many r's are in strawberry?")],
            new VllmChatOptions
            {
                ThinkingEnabled = false
            }))
        {
            updates.Add(update);
        }

        Assert.DoesNotContain(updates, static update => update is ReasoningChatResponseUpdate { Thinking: true });
        Assert.Contains(updates, static update => update.Text == "3");
    }

    private sealed class CaptureHandler(string responseJson, string mediaType = "application/json") : HttpMessageHandler
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
                Content = new StringContent(responseJson, Encoding.UTF8, mediaType)
            };
        }
    }
}
