using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("NativeAOT smoke requires reflection-based JSON serialization to be disabled.");
Console.WriteLine("JSON reflection disabled.");

var handler = new QueueingHandler();
using var httpClient = new HttpClient(handler);

await SmokeChatCompletionsAsync(httpClient, handler);
await SmokeArrayToolResultAsync(handler);
await SmokeArrayTextAndMediaPartsAsync(handler);
await SmokeResponsesAsync(httpClient, handler);
await SmokeAnthropicMessagesAsync(httpClient, handler);
await SmokeAnthropicJsonSchemaAsync(httpClient, handler);
await SmokeDeepseekV4HistoryAsync();
await SmokeSonnet55OpaqueAsync();
await SmokeDeepseekJsonObjectAsync();

Console.WriteLine("NativeAOT smoke completed.");

static async Task SmokeDeepseekJsonObjectAsync()
{
    const string model = "deepseek-v4.1-flash-1017";
    using var schemaDoc = JsonDocument.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""");
    foreach (var mode in new[] { VllmApiMode.ChatCompletions, VllmApiMode.AnthropicMessages, VllmApiMode.Responses })
    {
        var fake = new QueueingHandler();
        using var transport = new HttpClient(fake);
        using var client = new VllmDeepseekV4ChatClient("https://example.test", "offline-only", model, transport, mode);
        var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaDoc.RootElement, "probe"), ThinkingEnabled = false, MaxOutputTokens = 256 };
        foreach (var stream in new[] { false, true })
        {
            string payload;
            if (mode == VllmApiMode.AnthropicMessages)
                payload = stream ? """
                    data: {"type":"message_start","message":{"id":"n","model":"deepseek-v4.1-flash-1017","usage":{"input_tokens":1,"output_tokens":0}}}
                    data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}
                    data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"ok\":true}"}}
                    data: {"type":"content_block_stop","index":0}
                    data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}
                    data: {"type":"message_stop"}
                    """ : """{"id":"n","type":"message","role":"assistant","model":"deepseek-v4.1-flash-1017","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";
            else if (mode == VllmApiMode.Responses)
                payload = stream ? """
                    data: {"type":"response.created","response":{"id":"r","model":"deepseek-v4.1-flash-1017"}}
                    data: {"type":"response.output_text.delta","delta":"{\"ok\":true}"}
                    data: {"type":"response.completed","response":{"id":"r","status":"completed","model":"deepseek-v4.1-flash-1017","output":[],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}}
                    """ : """{"id":"r","status":"completed","model":"deepseek-v4.1-flash-1017","output":[{"id":"m","type":"message","role":"assistant","content":[{"type":"output_text","text":"{\"ok\":true}"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}""";
            else
                payload = stream ? """
                    data: {"id":"c","model":"deepseek-v4.1-flash-1017","choices":[{"index":0,"delta":{"content":"{\"ok\":true}"}}]}
                    data: {"id":"c","model":"deepseek-v4.1-flash-1017","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                    data: [DONE]
                    """ : """{"id":"c","model":"deepseek-v4.1-flash-1017","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";
            fake.Enqueue(stream ? Sse(payload) : Json(payload));
            ChatResponse response;
            if (stream)
            {
                List<ChatResponseUpdate> updates = [];
                await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "answer")], options)) updates.Add(update);
                response = updates.ToChatResponse();
            }
            else response = await client.GetResponseAsync([new(ChatRole.User, "answer")], options);
            using var requestDoc = JsonDocument.Parse(fake.LastRequestBody!);
            var request = requestDoc.RootElement;
            var format = mode switch
            {
                VllmApiMode.AnthropicMessages => request.GetProperty("output_config").GetProperty("format"),
                VllmApiMode.Responses => request.GetProperty("text").GetProperty("format"),
                _ => request.GetProperty("response_format")
            };
            if (format.GetProperty("type").GetString() != "json_object" || format.TryGetProperty("schema", out _)) throw new InvalidOperationException("DeepSeek JSON Object wire contract failed.");
            using var result = JsonDocument.Parse(response.Text);
            if (!result.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("DeepSeek JSON Object fake response missing.");
            if (response.AdditionalProperties?["vllm.structured_output.actual"] as string != "json_object" || !Equals(response.AdditionalProperties?["vllm.structured_output.provider_schema_enforced"], false)) throw new InvalidOperationException("DeepSeek JSON Object metadata lost.");
        }
    }
    Console.WriteLine("DeepSeek JSON Object: three protocols, streaming/nonstreaming, metadata and source-generated serialization passed.");
}

static async Task SmokeSonnet55OpaqueAsync()
{
    var handler = new QueueingHandler();
    using var http = new HttpClient(handler);
    using var client = new VllmClaudeChatClient("https://example.test", "offline-only", "claude-sonnet-5-5", http, VllmApiMode.AnthropicMessages);
    var options = new VllmChatOptions { ThinkingEnabled = false, MaxOutputTokens = 256 };
    const string blocks = """
        [{"type":"thinking","thinking":"thought思考","signature":"signed+/=","future":{"v":2}},
         {"type":"redacted_thinking","data":"redacted+/="},
         {"type":"tool_use","id":"c_sonnet","name":"echo","input":{"literal":"<ds_safety>keep</ds_safety>"}}]
        """;
    handler.Enqueue(Json("{\"id\":\"s\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-sonnet-5-5\",\"content\":" + blocks + ",\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":10,\"output_tokens\":7}}"));
    var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "start")], options);
    AssertOpaque(response);
    await ReplayAsync(response);

    handler.Enqueue(Sse("""
        data: {"type":"message_start","message":{"id":"s2","model":"claude-sonnet-5-5","usage":{"input_tokens":10,"output_tokens":0}}}
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","future":{"v":2}}}
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"thought思考"}}
        data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"signed+/="}}
        data: {"type":"content_block_stop","index":0}
        data: {"type":"content_block_start","index":1,"content_block":{"type":"redacted_thinking","data":"redacted+/="}}
        data: {"type":"content_block_stop","index":1}
        data: {"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"c_sonnet","name":"echo","input":{"literal":"<ds_safety>keep</ds_safety>"}}}
        data: {"type":"content_block_stop","index":2}
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":7}}
        data: {"type":"message_stop"}
        """));
    List<ChatResponseUpdate> updates = [];
    await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")], options)) updates.Add(update);
    var aggregated = updates.ToChatResponse();
    AssertOpaque(aggregated);
    if (aggregated.Usage?.InputTokenCount != 10 || aggregated.Usage.OutputTokenCount != 7)
        throw new InvalidOperationException("Sonnet streaming start/delta cumulative usage was lost.");
    await ReplayAsync(aggregated);

    static void AssertOpaque(ChatResponse response)
    {
        if (response.Messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Count(c => c.ProtectedData is not null) != 2)
            throw new InvalidOperationException("Sonnet opaque reasoning blocks were merged or lost.");
    }
    async Task ReplayAsync(ChatResponse response)
    {
        handler.Enqueue(Json("""{"id":"end","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"done"}],"stop_reason":"end_turn","usage":{"input_tokens":12,"output_tokens":1}}"""));
        List<ChatMessage> history = [new(ChatRole.User, "start")];
        history.AddRange(response.Messages);
        history.Add(new(ChatRole.Tool, [new FunctionResultContent("c_sonnet", "ok")]));
        _ = await client.GetResponseAsync(history, options);
        using var wire = JsonDocument.Parse(handler.LastRequestBody!);
        if (wire.RootElement.GetProperty("thinking").GetProperty("type").GetString() != "between_tools")
            throw new InvalidOperationException("Sonnet thinking=false wire mode is wrong.");
        var assistant = wire.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant");
        using var expected = JsonDocument.Parse(blocks);
        if (!JsonElement.DeepEquals(expected.RootElement, assistant.GetProperty("content")))
            throw new InvalidOperationException("Sonnet signed blocks, unknown fields, order or tool input changed on replay.");
    }
}

static async Task SmokeDeepseekV4HistoryAsync()
{
    var handler = new QueueingHandler();
    using var httpClient = new HttpClient(handler);
    const string literal = "<ds_safety>keep-in-argument</ds_safety>";
    using var blocks = JsonDocument.Parse("""
        [{"type":"thinking","thinking":"想 <ds_safety>hidden</ds_safety>Safe\n继续","trace":{"v":1}},
         {"type":"text","text":"答 <ds_safety>hidden</ds_safety>Safe\n完成"},
         {"type":"tool_use","id":"call_v4","name":"echo","input":{"literal":"<ds_safety>keep-in-argument</ds_safety>"}}]
        """);
    var replay = blocks.RootElement.EnumerateArray().Select(b => b.Clone()).ToArray();
    var original = replay.Select(b => b.GetRawText()).ToArray();
    var assistant = new ChatMessage(ChatRole.Assistant,
        [new FunctionCallContent("call_v4", "echo", new Dictionary<string, object?> { ["literal"] = literal })])
    {
        AdditionalProperties = new() { ["anthropic_content_blocks"] = replay }
    };
    using var client = new VllmDeepseekV4ChatClient("https://example.test/apps/anthropic", "test",
        "deepseek-v4.1-flash-1017", httpClient, VllmApiMode.AnthropicMessages);
    handler.Enqueue(Json("""
        {"id":"v4","type":"message","role":"assistant","model":"deepseek-v4.1-flash-1017","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
        """));
    _ = await client.GetResponseAsync([
        new ChatMessage(ChatRole.User, "<ds_safety>user material</ds_safety>"), assistant,
        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_v4", "ok")])
    ], new VllmChatOptions { ThinkingEnabled = false, MaxOutputTokens = 256 });
    using var request = JsonDocument.Parse(handler.LastRequestBody!);
    var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToArray();
    var content = messages.Single(m => m.GetProperty("role").GetString() == "assistant").GetProperty("content");
    if (content[0].GetProperty("thinking").GetString() != "想 继续" ||
        content[1].GetProperty("text").GetString() != "答 完成" ||
        content[0].GetProperty("trace").GetProperty("v").GetInt32() != 1 ||
        content[2].GetProperty("input").GetProperty("literal").GetString() != literal ||
        content[2].GetProperty("id").GetString() != "call_v4")
        throw new InvalidOperationException("V4 NativeAOT history filtering lost text, unknown properties or tool arguments.");
    if (!messages[0].GetProperty("content").GetRawText().Contains("user material") ||
        !messages[0].GetProperty("content").GetRawText().Contains("ds_safety"))
        throw new InvalidOperationException("V4 NativeAOT filtering changed user material.");
    if (!replay.Select(b => b.GetRawText()).SequenceEqual(original))
        throw new InvalidOperationException("V4 NativeAOT filtering mutated caller replay blocks.");
    Console.WriteLine("DeepSeek V4 native replay (thinking/text/tool/raw) passed.");
}

static async Task SmokeAnthropicJsonSchemaAsync(HttpClient httpClient, QueueingHandler handler)
{
    using var schema = JsonDocument.Parse("""
        {"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}
        """);
    using var client = new VllmGlmChatClient("https://example.test/v1/messages", "test", "smoke", httpClient, VllmApiMode.AnthropicMessages);
    var options = new ChatOptions { MaxOutputTokens = 256, ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement, "probe") };
    handler.Enqueue(Json("""
        {"id":"msg_schema","type":"message","role":"assistant","model":"smoke","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
        """));
    _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Return JSON.")], options);
    AssertSchema();
    handler.Enqueue(Sse("""
        data: {"type":"message_start","message":{"id":"msg_schema_s","type":"message","role":"assistant","model":"smoke","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"ok\":true}"}}
        data: {"type":"content_block_stop","index":0}
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}
        data: {"type":"message_stop"}
        """));
    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Return JSON.")], options)) { }
    AssertSchema();

    void AssertSchema()
    {
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var format = body.RootElement.GetProperty("output_config").GetProperty("format");
        if (format.GetProperty("type").GetString() != "json_schema" || !JsonElement.DeepEquals(schema.RootElement, format.GetProperty("schema")))
            throw new InvalidOperationException("NativeAOT Anthropic output schema was lost or changed.");
        if (body.RootElement.TryGetProperty("response_format", out _))
            throw new InvalidOperationException("OpenAI response_format must not leak into native Anthropic requests.");
    }
}

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

static async Task SmokeArrayTextAndMediaPartsAsync(QueueingHandler handler)
{
    using var mediaHttpClient = new HttpClient(handler, disposeHandler: false);
    using var client = new VllmQwen3NextChatClient("https://example.test/v1", "test", "qwen3.8-27b", mediaHttpClient);
    const string answer = "[{\"key\":\"attr:a\",\"verdict\":\"confirmed\"}]";
    handler.Enqueue(Json("""
        {"id":"chat-array-text","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
        """));
    _ = await client.GetResponseAsync([
        new ChatMessage(ChatRole.User, [new UriContent("https://example.test/a.jpg", "image/jpeg"), new TextContent("Describe.")]),
        new ChatMessage(ChatRole.Assistant, answer),
        new ChatMessage(ChatRole.User, "Return a JSON object.")
    ]);
    using var request = JsonDocument.Parse(handler.LastRequestBody!);
    var messages = request.RootElement.GetProperty("messages");
    var media = messages[0].GetProperty("content");
    if (media.ValueKind != JsonValueKind.Array || media[0].GetProperty("type").GetString() != "image_url")
        throw new InvalidOperationException("NativeAOT media input must remain content parts.");
    var assistant = messages[1].GetProperty("content");
    if (assistant.ValueKind != JsonValueKind.String || assistant.GetString() != answer)
        throw new InvalidOperationException("NativeAOT array-shaped assistant text must remain JSON text.");
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
