using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Mimo;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class MimoProviderCompatibilityTests
{
    [Fact]
    public async Task Xiaomi_Request_UsesApiKey_Header_And_Mimo_TopLevel_Fields()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1773939455,
  "model": "mimo-v2-pro",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "你好，我是 MiMo。",
        "reasoning_content": "I should introduce myself."
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmMimoChatClient(
            "https://api.xiaomimimo.com/v1{1}",
            "mimo-key",
            "mimo-v2-pro",
            httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "please introduce yourself")
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 1024,
            Temperature = 1.0f,
            TopP = 0.95f
        };

        var response = await client.GetResponseAsync(messages, options);

        Assert.Equal("https://api.xiaomimimo.com/v1/chat/completions", handler.LastRequestUri?.ToString());
        Assert.NotNull(handler.LastRequestHeaders);
        Assert.True(handler.LastRequestHeaders!.TryGetValues("api-key", out var apiKeys));
        Assert.Equal("mimo-key", apiKeys.Single());
        Assert.False(handler.LastRequestHeaders.Contains("Authorization"));

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("mimo-v2-pro", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(1024, doc.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(1.0f, doc.RootElement.GetProperty("temperature").GetSingle());
        Assert.Equal(0.95f, doc.RootElement.GetProperty("top_p").GetSingle());
        Assert.False(doc.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(doc.RootElement.TryGetProperty("options", out _));

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.Equal("I should introduce myself.", reasoningResponse.Reason);
    }

    [Theory]
    [InlineData(true, "enabled")]
    [InlineData(false, "disabled")]
    public async Task Xiaomi_Request_Uses_ExtraBody_Thinking_Toggle(bool thinkingEnabled, string expectedType)
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1773939455,
  "model": "mimo-v2-pro",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "ok"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmMimoChatClient(
            "https://api.xiaomimimo.com/v1",
            "mimo-key",
            "mimo-v2-pro",
            httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello")
        };

        var options = new VllmChatOptions
        {
            ThinkingEnabled = thinkingEnabled
        };

        _ = await client.GetResponseAsync(messages, options);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("extra_body", out var extraBody));
        Assert.True(extraBody.TryGetProperty("thinking", out var thinking));
        Assert.True(thinking.TryGetProperty("type", out var type));
        Assert.Equal(expectedType, type.GetString());
        Assert.False(doc.RootElement.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task Xiaomi_Response_Hides_Reasoning_When_Thinking_Is_Disabled()
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1773939455,
  "model": "mimo-v2-pro",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "{\"greeting\":\"hello\"}",
        "reasoning_content": "internal reasoning"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var client = new VllmMimoChatClient(
            "https://api.xiaomimimo.com/v1",
            "mimo-key",
            "mimo-v2-pro",
            httpClient);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new VllmChatOptions { ThinkingEnabled = false });

        var reasoningResponse = Assert.IsType<ReasoningChatResponse>(response);
        Assert.Equal(string.Empty, reasoningResponse.Reason);
        Assert.Equal("{\"greeting\":\"hello\"}", response.Text);
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? LastRequestHeaders { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestHeaders = request.Headers;
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
