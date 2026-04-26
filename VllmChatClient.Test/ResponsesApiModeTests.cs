using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class ResponsesApiModeTests
{
    [Fact]
    public async Task BaseClient_ResponsesMode_ShouldPostToResponsesEndpoint()
    {
        string? requestJson = null;
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestUri = request.RequestUri;
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "resp-1",
                  "object": "response",
                  "created_at": 1,
                  "status": "completed",
                  "model": "test-model",
                  "output": [
                    {
                      "id": "msg-1",
                      "type": "message",
                      "role": "assistant",
                      "content": [{ "type": "output_text", "text": "hello" }]
                    }
                  ],
                  "usage": { "input_tokens": 2, "output_tokens": 3, "total_tokens": 5 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", httpClient, VllmApiMode.Responses);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("/v1/responses", requestUri?.AbsolutePath);
        Assert.Equal("hello", response.Text);
        Assert.Equal(2, response.Usage?.InputTokenCount);
        Assert.NotNull(requestJson);

        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("hi", root.GetProperty("input")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task BaseClient_ResponsesMode_ShouldParseFunctionCallOutput()
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("""
            {
              "id": "resp-2",
              "object": "response",
              "created_at": 1,
              "status": "completed",
              "model": "test-model",
              "output": [
                {
                  "id": "fc-1",
                  "type": "function_call",
                  "name": "GetWeather",
                  "call_id": "call-1",
                  "arguments": "{\"city\":\"Singapore\"}"
                }
              ]
            }
            """));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", httpClient, VllmApiMode.Responses);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather")]);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[0].Contents));

        Assert.Equal("/v1/responses", requestUri?.AbsolutePath);
        Assert.Equal("GetWeather", call.Name);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task BaseClient_ResponsesModeStreaming_ShouldReadSemanticEvents()
    {
        var handler = new CaptureHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                data: {"type":"response.created","sequence_number":0,"response":{"id":"resp-stream","model":"test-model","created_at":1}}

                data: {"type":"response.output_text.delta","sequence_number":1,"item_id":"msg-1","output_index":0,"content_index":0,"delta":"hel"}

                data: {"type":"response.output_text.delta","sequence_number":2,"item_id":"msg-1","output_index":0,"content_index":0,"delta":"lo"}

                data: {"type":"response.completed","sequence_number":3,"response":{"id":"resp-stream","model":"test-model","created_at":1,"status":"completed","usage":{"input_tokens":1,"output_tokens":2,"total_tokens":3}}}

                data: [DONE]

                """,
                Encoding.UTF8,
                "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", httpClient, VllmApiMode.Responses);

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

    private sealed class TestVllmChatClient : VllmBaseChatClient
    {
        public TestVllmChatClient(string endpoint, HttpClient httpClient, VllmApiMode apiMode)
            : base(endpoint, token: null, modelId: "test-model", httpClient, apiMode)
        {
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
