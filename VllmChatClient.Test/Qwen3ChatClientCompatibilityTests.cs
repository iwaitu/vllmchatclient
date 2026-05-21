using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class Qwen3ChatClientCompatibilityTests
{
    private const string ChatResponse = """
{
  "id": "chatcmpl-qwen3-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3-32b",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "{\"ok\":true}"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 1,
    "completion_tokens": 1,
    "total_tokens": 2
  }
}
""";

    [Fact]
    public async Task Qwen3ChatClient_WithThinkingEnabled_DoesNotWriteQwen3ThinkingRequestFields()
    {
        var handler = new CaptureJsonHandler(ChatResponse);
        using var httpClient = new HttpClient(handler);
        using var client = new VllmQwen3ChatClient(
            "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}",
            "fake-token",
            "qwen3-32b",
            httpClient);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new Qwen3ChatOptions { MaxOutputTokens = 128 });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        var options = root.GetProperty("options");

        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", handler.LastRequestUri!.ToString());
        Assert.False(root.TryGetProperty("enable_thinking", out _));
        Assert.False(root.TryGetProperty("chat_template_kwargs", out _));
        Assert.DoesNotContain(
            root.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "system" &&
                message.GetProperty("content").GetString()?.Contains("/no_think", StringComparison.Ordinal) == true);
        Assert.Equal(128, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(20, options.GetProperty("top_k").GetInt32());
        Assert.InRange(options.GetProperty("temperature").GetSingle(), 0.949f, 0.951f);
        Assert.InRange(options.GetProperty("top_p").GetSingle(), 0.899f, 0.901f);
    }

    [Fact]
    public async Task Qwen3ChatClient_WithNoThinking_InsertsNoThinkInSystemPromptWithoutMutatingMessages()
    {
        var handler = new CaptureJsonHandler(ChatResponse);
        using var httpClient = new HttpClient(handler);
        using var client = new VllmQwen3ChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3-32b", httpClient);
        var systemMessage = new ChatMessage(ChatRole.System, "你是一个智能助手");
        var userMessage = new ChatMessage(ChatRole.User, "输出 JSON");
#pragma warning disable CS0618
        var options = new Qwen3ChatOptions { NoThinking = true };
#pragma warning restore CS0618

        await client.GetResponseAsync([systemMessage, userMessage], options);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        var messages = root.GetProperty("messages");
        var systemText = messages[0].GetProperty("content").GetString();
        var requestText = messages[1].GetProperty("content").GetString();

        Assert.False(options.ThinkingEnabled);
        Assert.False(root.TryGetProperty("enable_thinking", out _));
        Assert.False(root.TryGetProperty("chat_template_kwargs", out _));
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("你是一个智能助手", systemText);
        Assert.Contains("/no_think", systemText);
        Assert.Equal("输出 JSON", requestText);
        Assert.Equal("你是一个智能助手", systemMessage.Contents.OfType<TextContent>().Single().Text);
        Assert.Equal("输出 JSON", userMessage.Contents.OfType<TextContent>().Single().Text);
    }

    [Fact]
    public async Task Qwen3ChatClient_StreamingThinkTags_AreExposedAsReasoningUpdates()
    {
        const string streamPayload = """
data: {"choices":[{"delta":{"role":"assistant","content":"<think>"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3-32b","id":"chatcmpl-stream"}
data: {"choices":[{"delta":{"content":"先思考"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3-32b","id":"chatcmpl-stream"}
data: {"choices":[{"delta":{"content":"</think>\n最终答案"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3-32b","id":"chatcmpl-stream"}
data: {"choices":[{"delta":{"content":""},"finish_reason":"stop","index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3-32b","id":"chatcmpl-stream"}
data: [DONE]
""";

        var handler = new CaptureJsonHandler(ChatResponse, streamPayload);
        using var httpClient = new HttpClient(handler);
        using var client = new VllmQwen3ChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3-32b", httpClient);
        var reasoning = string.Empty;
        var answer = string.Empty;

        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], new Qwen3ChatOptions()))
        {
            if (update is ReasoningChatResponseUpdate { Thinking: true } reasoningUpdate)
            {
                reasoning += reasoningUpdate.Text;
            }
            else
            {
                answer += update.Text;
            }
        }

        Assert.Equal("先思考", reasoning);
        Assert.Contains("最终答案", answer);
        Assert.DoesNotContain("<think>", answer);
        Assert.DoesNotContain("</think>", answer);
    }

    private sealed class CaptureJsonHandler(string jsonPayload, string? streamPayload = null) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    streamPayload is not null
                        ? streamPayload
                        : jsonPayload,
                    Encoding.UTF8,
                    streamPayload is not null
                        ? "text/event-stream"
                        : "application/json")
            };
        }
    }
}
