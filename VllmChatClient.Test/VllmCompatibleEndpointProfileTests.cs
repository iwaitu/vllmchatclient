using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class VllmCompatibleEndpointProfileTests
{
    [Fact]
    public async Task BaseClient_Profile_AppliesHeadersThinkingExtraBodyAndTopLevelGenerationOptions()
    {
        string? requestJson = null;
        HttpRequestMessage? capturedRequest = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            capturedRequest = request;
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "chatcmpl-profile",
                  "object": "chat.completion",
                  "created": 1,
                  "model": "new-main-model",
                  "choices": [
                    {
                      "index": 0,
                      "message": { "role": "assistant", "content": "ok" },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
                }
                """);
        });

        var profile = new VllmCompatibleEndpointProfile
        {
            ProviderName = "new-provider",
            TokenHeaderName = "api-key",
            DefaultHeaders = new Dictionary<string, string>
            {
                ["x-provider-mode"] = "compatible"
            },
            ExtraBody = new Dictionary<string, object?>
            {
                ["provider_hint"] = "new-main"
            },
            ThinkingParameter = VllmCompatibleThinkingParameter.ExtraBodyThinkingType,
            UseTopLevelGenerationOptions = true,
            UseMaxCompletionTokens = true
        };

        using var httpClient = new HttpClient(handler);
        using var client = new VllmBaseChatClient(
            "https://api.example.test/v1",
            "test-token",
            "new-main-model",
            httpClient,
            profile: profile);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new VllmChatOptions
            {
                ThinkingEnabled = true,
                Temperature = 0.2f,
                TopP = 0.8f,
                MaxOutputTokens = 128
            });

        Assert.Equal("ok", response.Text);
        Assert.Equal("https://api.example.test/v1/chat/completions", capturedRequest?.RequestUri?.ToString());
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.TryGetValues("api-key", out var apiKeys));
        Assert.Equal("test-token", Assert.Single(apiKeys));
        Assert.True(capturedRequest.Headers.TryGetValues("x-provider-mode", out var providerModes));
        Assert.Equal("compatible", Assert.Single(providerModes));
        Assert.Null(capturedRequest.Headers.Authorization);

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        Assert.Equal("new-main-model", root.GetProperty("model").GetString());
        Assert.Equal(128, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(0.2f, root.GetProperty("temperature").GetSingle());
        Assert.Equal(0.8f, root.GetProperty("top_p").GetSingle());
        Assert.False(root.TryGetProperty("max_tokens", out _));
        Assert.False(root.TryGetProperty("options", out _));

        var extraBody = root.GetProperty("extra_body");
        Assert.Equal("new-main", extraBody.GetProperty("provider_hint").GetString());
        Assert.Equal("enabled", extraBody.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task BaseClient_Profile_ResponsesMode_CarriesExtraBodyDefaults()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "resp-profile",
                  "object": "response",
                  "created_at": 1,
                  "status": "completed",
                  "model": "new-main-model",
                  "output": [
                    {
                      "id": "msg-1",
                      "type": "message",
                      "role": "assistant",
                      "content": [{ "type": "output_text", "text": "ok" }]
                    }
                  ]
                }
                """);
        });

        var profile = new VllmCompatibleEndpointProfile
        {
            ExtraBody = new Dictionary<string, object?>
            {
                ["compatibility_mode"] = "responses"
            }
        };

        using var httpClient = new HttpClient(handler);
        using var client = new VllmBaseChatClient(
            "https://api.example.test/v1/chat/completions",
            "test-token",
            "new-main-model",
            httpClient,
            VllmApiMode.Responses,
            profile);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("ok", response.Text);
        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        Assert.Equal("responses", doc.RootElement.GetProperty("compatibility_mode").GetString());
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
