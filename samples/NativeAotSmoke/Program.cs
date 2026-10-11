using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

var handler = new QueueingHandler();
using var httpClient = new HttpClient(handler);

await SmokeChatCompletionsAsync(httpClient, handler);
await SmokeArrayToolResultAsync(handler);
await SmokeResponsesAsync(httpClient, handler);
await SmokeAnthropicMessagesAsync(httpClient, handler);

Console.WriteLine("NativeAOT smoke completed.");

static async Task SmokeArrayToolResultAsync(QueueingHandler handler)
{
    using var toolHttpClient = new HttpClient(handler, disposeHandler: false);
    using var client = new VllmQwen3NextChatClient("https://example.test/v1", "test", "smoke", toolHttpClient);
    const string result = "[{\"value\":2.5,\"evidence\":\"document\"}]";
    handler.Enqueue(Json("""
        {"id":"chat-tool-array","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
        """));
    _ = await client.GetResponseAsync([
        new ChatMessage(ChatRole.User, "Read the document."),
        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-array", "read_document", new Dictionary<string, object?>())]),
        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-array", result)])
    ]);
    using var request = JsonDocument.Parse(handler.LastRequestBody!);
    var content = request.RootElement.GetProperty("messages")[2].GetProperty("content");
    if (content.ValueKind != JsonValueKind.String || content.GetString() != result)
        throw new InvalidOperationException("NativeAOT tool array result must remain JSON text.");
}

static async Task SmokeChatCompletionsAsync(HttpClient httpClient, QueueingHandler handler)
{
    var client = new VllmGlmChatClient("https://example.test/v1/chat/completions", "test", "smoke", httpClient);

    handler.Enqueue(Json("""
        {"id":"chatcmpl-1","object":"chat.completion","created":0,"model":"smoke","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """));
    _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

    handler.Enqueue(Sse("""
        data: {"id":"chatcmpl-s","object":"chat.completion.chunk","created":0,"model":"smoke","choices":[{"index":0,"delta":{"role":"assistant"},"finish_reason":null}]}
        data: {"id":"chatcmpl-s","object":"chat.completion.chunk","created":0,"model":"smoke","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}
        data: {"id":"chatcmpl-s","object":"chat.completion.chunk","created":0,"model":"smoke","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}
        data: [DONE]
        """));
    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
    {
    }
}

static async Task SmokeResponsesAsync(HttpClient httpClient, QueueingHandler handler)
{
    var client = new VllmGlmChatClient("https://example.test/v1/responses", "test", "smoke", httpClient, VllmApiMode.Responses);

    handler.Enqueue(Json("""
        {"id":"resp_1","object":"response","created_at":0,"model":"smoke","status":"completed","output":[{"id":"msg_1","type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}
        """));
    _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

    handler.Enqueue(Sse("""
        data: {"type":"response.output_text.delta","delta":"ok","response":{"id":"resp_s","model":"smoke"}}
        data: {"type":"response.completed","response":{"id":"resp_s","object":"response","created_at":0,"model":"smoke","status":"completed","output_text":"ok","usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}}
        data: [DONE]
        """));
    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
    {
    }
}

static async Task SmokeAnthropicMessagesAsync(HttpClient httpClient, QueueingHandler handler)
{
    var client = new VllmGlmChatClient("https://example.test/v1/messages", "test", "smoke", httpClient, VllmApiMode.AnthropicMessages);

    handler.Enqueue(Json("""
        {"id":"msg_1","type":"message","role":"assistant","model":"smoke","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
        """));
    _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

    handler.Enqueue(Sse("""
        data: {"type":"message_start","message":{"id":"msg_s","type":"message","role":"assistant","model":"smoke","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":"ok"}}
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":1,"output_tokens":1}}
        data: [DONE]
        """));
    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
    {
    }
}

static HttpResponseMessage Json(string content)
    => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

static HttpResponseMessage Sse(string content)
    => new(HttpStatusCode.OK)
    {
        Content = new StringContent(NormalizeSse(content), Encoding.UTF8, "text/event-stream")
    };

static string NormalizeSse(string content)
{
    var lines = content.Trim().Split('\n');
    var builder = new StringBuilder();
    foreach (var line in lines)
    {
        builder.AppendLine(line.Trim());
        builder.AppendLine();
    }

    return builder.ToString();
}

internal sealed class QueueingHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpResponseMessage> _responses = new();
    public string? LastRequestBody { get; private set; }

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (!_responses.TryDequeue(out var response))
        {
            response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent($"No queued response for {request.Method} {request.RequestUri}")
            };
        }

        return response;
    }
}
