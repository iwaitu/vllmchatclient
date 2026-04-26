using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class AnthropicApiModeTests
{
    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldPostMessagesRequest()
    {
        string? requestJson = null;
        Uri? requestUri = null;
        HttpRequestHeadersSnapshot? headers = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestUri = request.RequestUri;
            headers = new HttpRequestHeadersSnapshot(
                request.Headers.Contains("x-api-key"),
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.SingleOrDefault() : null);
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-1",
                  "type": "message",
                  "role": "assistant",
                  "model": "claude-test",
                  "content": [{ "type": "text", "text": "hello" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 2, "output_tokens": 3 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", "test-key", httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "You are concise."),
                new ChatMessage(ChatRole.User, "hi")
            ],
            new ChatOptions { MaxOutputTokens = 123 });

        Assert.Equal("/v1/messages", requestUri?.AbsolutePath);
        Assert.True(headers?.HasApiKey);
        Assert.Equal("2023-06-01", headers?.AnthropicVersion);
        Assert.Equal("hello", response.Text);
        Assert.Equal(5, response.Usage?.TotalTokenCount);

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        Assert.Equal("claude-test", root.GetProperty("model").GetString());
        Assert.Equal(123, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("You are concise.", root.GetProperty("system").GetString());
        Assert.Equal("hi", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldParseToolUse()
    {
        var handler = new CaptureHttpMessageHandler(_ => Task.FromResult(JsonResponse("""
            {
              "id": "msg-2",
              "type": "message",
              "role": "assistant",
              "model": "claude-test",
              "content": [
                {
                  "type": "tool_use",
                  "id": "toolu-1",
                  "name": "GetWeather",
                  "input": { "city": "Singapore" }
                }
              ],
              "stop_reason": "tool_use",
              "usage": { "input_tokens": 4, "output_tokens": 5 }
            }
            """)));

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", null, httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather")]);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[0].Contents));

        Assert.Equal("GetWeather", call.Name);
        Assert.Equal("toolu-1", call.CallId);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task BaseClient_AnthropicModeStreaming_ShouldReadAnthropicEvents()
    {
        var handler = new CaptureHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                event: message_start
                data: {"type":"message_start","message":{"id":"msg-stream","type":"message","role":"assistant","model":"claude-test","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}

                event: content_block_start
                data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hel"}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"lo"}}

                event: message_delta
                data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":1,"output_tokens":2}}

                event: message_stop
                data: {"type":"message_stop"}

                """,
                Encoding.UTF8,
                "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", null, httpClient, VllmApiMode.AnthropicMessages);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(update);
        }

        Assert.Equal("hello", string.Concat(updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(c => c.Text)));
        Assert.Contains(updates, u => u is UsageChatResponseUpdate usage && usage.Usage?.TotalTokenCount == 3);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record HttpRequestHeadersSnapshot(bool HasApiKey, string? AnthropicVersion);

    private sealed class TestVllmChatClient : VllmBaseChatClient
    {
        public TestVllmChatClient(string endpoint, string? token, HttpClient httpClient, VllmApiMode apiMode)
            : base(endpoint, token, modelId: "claude-test", httpClient, apiMode)
        {
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
