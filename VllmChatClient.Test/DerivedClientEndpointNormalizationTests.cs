using Microsoft.Extensions.AI;
using System.Net;
using System.Text;

namespace VllmChatClient.Test;

public class DerivedClientEndpointNormalizationTests
{
    [Fact]
    public async Task Qwen3NextClient_PlainV1Endpoint_UsesChatCompletionsPath()
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-1\",\"created\":1,\"model\":\"qwen3.6-plus\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmQwen3NextChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1", "test-key", "qwen3.6-plus", httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new VllmChatOptions { ThinkingEnabled = true });

        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", requestUri?.ToString());
        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task DeepseekClient_PlainV1Endpoint_UsesMessagesPathInAnthropicMode()
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
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com/anthropic/v1", "test-key", httpClient: httpClient, apiMode: VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("https://api.deepseek.com/anthropic/v1/messages", requestUri?.ToString());
        Assert.Equal("hello", response.Text);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/{0}/{1}", "https://openrouter.ai/api/v1/chat/completions")]
    public async Task OpenAiDerivedClients_CompatibleEndpoints_UseChatCompletionsPath(string endpoint, string expectedRequestUri)
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-2\",\"created\":1,\"model\":\"gpt-oss-120b\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmOpenAiGptClient(endpoint, "test-key", "openai/gpt-5.2-codex", httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(expectedRequestUri, requestUri?.ToString());
        Assert.Equal("ok", response.Text);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/{0}/{1}", "https://openrouter.ai/api/v1/chat/completions")]
    public async Task GptOssAndNemotronClients_CompatibleEndpoints_UseChatCompletionsPath(string endpoint, string expectedRequestUri)
    {
        Uri? gptOssRequestUri = null;
        Uri? nemotronRequestUri = null;

        var gptOssHandler = new CaptureHttpMessageHandler(request =>
        {
            gptOssRequestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-3\",\"created\":1,\"model\":\"gpt-oss-120b\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        var nemotronHandler = new CaptureHttpMessageHandler(request =>
        {
            nemotronRequestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-4\",\"created\":1,\"model\":\"nvidia/nemotron-3-super-120b-a12b:free\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var gptOssHttpClient = new HttpClient(gptOssHandler);
        using var nemotronHttpClient = new HttpClient(nemotronHandler);
        using var gptOssClient = new Microsoft.Extensions.AI.VllmChatClient.GptOss.VllmGptOssChatClient(endpoint, "test-key", "gpt-oss-120b", gptOssHttpClient);
        using var nemotronClient = new VllmNemotronChatClient(endpoint, "test-key", httpClient: nemotronHttpClient);

        _ = await gptOssClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        _ = await nemotronClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(expectedRequestUri, gptOssRequestUri?.ToString());
        Assert.Equal(expectedRequestUri, nemotronRequestUri?.ToString());
    }

    [Theory]
    [InlineData("https://api.xiaomimimo.com/v1", "https://api.xiaomimimo.com/v1/chat/completions")]
    [InlineData("https://api.xiaomimimo.com/{0}/{1}", "https://api.xiaomimimo.com/v1/chat/completions")]
    public async Task MimoClient_CompatibleEndpoints_UseChatCompletionsPath(string endpoint, string expectedRequestUri)
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"id\":\"resp-5\",\"created\":1,\"model\":\"mimo-v2-pro\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new Microsoft.Extensions.AI.VllmChatClient.Mimo.VllmMimoChatClient(endpoint, "test-key", "mimo-v2-pro", httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(expectedRequestUri, requestUri?.ToString());
        Assert.Equal("ok", response.Text);
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    public async Task Gemma4Client_ResolvesGoogleNativeAndCompatibleEndpoints(string endpoint, string expectedRequestUri)
    {
        Uri? requestUri = null;
        var handler = new CaptureHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(endpoint.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse("""
                    {
                      "candidates": [
                        {
                          "content": {
                            "role": "model",
                            "parts": [
                              {
                                "text": "ok"
                              }
                            ]
                          },
                          "finishReason": "STOP",
                          "index": 0
                        }
                      ]
                    }
                    """)
                : JsonResponse("{\"id\":\"resp-6\",\"created\":1,\"model\":\"gemma-4-31b-it\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmGemma4ChatClient(endpoint, "test-key", "gemma-4-31b-it", httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(expectedRequestUri, requestUri?.ToString());
        Assert.Equal("ok", response.Text);
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
