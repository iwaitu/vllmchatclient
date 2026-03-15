using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class NemotronProviderCompatibilityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenRouter_Request_UsesReasoningToggle(bool thinkingEnabled)
    {
        const string responseJson = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "nvidia/nemotron-3-super-120b-a12b:free",
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
        var client = new VllmNemotronChatClient(
            "https://openrouter.ai/api/v1",
            "openrouter-key",
            httpClient: httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "How many r's are in strawberry?")
        };

        var options = new VllmChatOptions
        {
            ThinkingEnabled = thinkingEnabled
        };

        _ = await client.GetResponseAsync(messages, options);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequestUri!.ToString());

        Assert.NotNull(httpClient.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", httpClient.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("openrouter-key", httpClient.DefaultRequestHeaders.Authorization.Parameter);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("enabled", out var enabled));
        Assert.Equal(thinkingEnabled, enabled.GetBoolean());
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
