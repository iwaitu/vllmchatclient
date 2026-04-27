using Microsoft.Extensions.AI;
using System.Net;
using System.Text;

namespace VllmChatClient.Test;

public class DeepseekEndpointProcessingTests
{
    [Fact]
    public async Task DeepseekClient_ChatCompletionsMode_AppendsChatCompletionsPath()
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-1\",\"created\":1,\"model\":\"deepseek-v4-flash\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com", "test-key", httpClient: httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("https://api.deepseek.com/v1/chat/completions", requestUri?.ToString());
        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task DeepseekClient_AnthropicMode_AppendsMessagesPath()
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("""
                {
                  "id": "msg-1",
                  "type": "message",
                  "role": "assistant",
                  "model": "deepseek-v4-flash",
                  "content": [{ "type": "text", "text": "hello" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 2, "output_tokens": 3 }
                }
                """));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com/anthropic", "test-key", httpClient: httpClient, apiMode: VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("https://api.deepseek.com/anthropic/v1/messages", requestUri?.ToString());
        Assert.Equal("hello", response.Text);
    }

    [Fact]
    public async Task DeepseekClient_ExplicitChatCompletionsPath_IsPreserved()
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-2\",\"created\":1,\"model\":\"deepseek-v4-flash\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com/v1/chat/completions", "test-key", httpClient: httpClient);

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("https://api.deepseek.com/v1/chat/completions", requestUri?.ToString());
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
