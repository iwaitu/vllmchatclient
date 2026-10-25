using Microsoft.Extensions.AI;
using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>Root-owned offline wire and public-interface contract. No environment credentials or network.</summary>
public class DeepseekV41JsonObjectCompatibilityTests
{
    private const string Model = "deepseek-v4.1-flash-1017";
    private const string Marker = "VllmDeepSeekJsonObjectSchema";
    private const string SchemaText = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""";
    private static JsonElement Element(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class Handler(VllmApiMode mode, bool streaming) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Bodies) Bodies.Add(body);
            return new(HttpStatusCode.OK) { Content = new StringContent(Reply(mode, streaming), Encoding.UTF8, streaming ? "text/event-stream" : "application/json") };
        }
    }

    private static string Reply(VllmApiMode mode, bool streaming)
    {
        const string content = "{\"ok\":true}";
        if (mode == VllmApiMode.AnthropicMessages)
        {
            if (!streaming) return """{"id":"native","type":"message","role":"assistant","model":"deepseek-v4.1-flash-1017","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}}""";
            return string.Join("\n\n", new[]
            {
                "data: {\"type\":\"message_start\",\"message\":{\"id\":\"native\",\"model\":\"deepseek-v4.1-flash-1017\",\"usage\":{\"input_tokens\":10,\"output_tokens\":0}}}",
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
                "data: " + JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = content } }),
                "data: {\"type\":\"content_block_stop\",\"index\":0}",
                "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":3}}",
                "data: {\"type\":\"message_stop\"}", ""
            });
        }
        if (mode == VllmApiMode.Responses)
        {
            var full = new { id = "resp", @object = "response", status = "completed", model = Model,
                output = new[] { new { id = "msg", type = "message", role = "assistant", content = new[] { new { type = "output_text", text = content } } } },
                usage = new { input_tokens = 10, output_tokens = 3, total_tokens = 13 } };
            if (!streaming) return JsonSerializer.Serialize(full);
            return "data: " + JsonSerializer.Serialize(new { type = "response.created", response = new { id = "resp", model = Model } }) + "\n\n"
                + "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = content }) + "\n\n"
                + "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = full }) + "\n\n";
        }
        if (!streaming) return JsonSerializer.Serialize(new { id = "chat", model = Model,
            choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content } } },
            usage = new { prompt_tokens = 10, completion_tokens = 3, total_tokens = 13 } });
        return "data: " + JsonSerializer.Serialize(new { id = "chat", model = Model, choices = new[] { new { index = 0, delta = new { content } } } }) + "\n\n"
            + "data: {\"id\":\"chat\",\"model\":\"deepseek-v4.1-flash-1017\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}\n\ndata: [DONE]\n\n";
    }

    private static VllmDeepseekV4ChatClient Client(VllmApiMode mode, HttpClient http, string model = Model)
        => new("https://offline.test", "offline-only", model, http, mode);

    private static async Task<ChatResponse> Invoke(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options, bool stream, List<ChatResponseUpdate>? updates = null)
    {
        if (!stream) return await client.GetResponseAsync(messages, options);
        updates ??= [];
        await foreach (var update in client.GetStreamingResponseAsync(messages, options)) updates.Add(update);
        return updates.ToChatResponse();
    }

    private static string Text(JsonElement content) => content.ValueKind == JsonValueKind.String ? content.GetString()! :
        string.Join("\n", content.EnumerateArray().Select(x => x.TryGetProperty("text", out var t) ? t.GetString() : ""));

    private static string SystemText(JsonElement root, VllmApiMode mode)
        => mode == VllmApiMode.AnthropicMessages ? Text(root.GetProperty("system")) :
            string.Join("\n", root.GetProperty(mode == VllmApiMode.Responses ? "input" : "messages").EnumerateArray()
                .Where(m => m.TryGetProperty("role", out var r) && r.GetString() == "system").Select(m => Text(m.GetProperty("content"))));

    private static void AssertWire(string body, VllmApiMode mode, bool schema)
    {
        var root = Element(body);
        var format = mode switch
        {
            VllmApiMode.AnthropicMessages => root.GetProperty("output_config").GetProperty("format"),
            VllmApiMode.Responses => root.GetProperty("text").GetProperty("format"),
            _ => root.GetProperty("response_format")
        };
        Assert.Equal("""{"type":"json_object"}""", format.GetRawText());
        Assert.False(root.TryGetProperty("format", out _));
        Assert.False(root.TryGetProperty("output_format", out _));
        if (mode != VllmApiMode.ChatCompletions) Assert.False(root.TryGetProperty("response_format", out _));
        if (root.TryGetProperty("extra_body", out var extra)) Assert.False(extra.TryGetProperty("structured_outputs", out _));
        var system = SystemText(root, mode);
        Assert.Equal(1, system.Split(Marker, StringSplitOptions.None).Length - 1);
        Assert.Contains("JSON", system, StringComparison.OrdinalIgnoreCase);
        if (schema)
        {
            var afterMarker = system[(system.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(afterMarker[afterMarker.IndexOf('{')..]));
            using var parsed = JsonDocument.ParseValue(ref reader);
            Assert.True(JsonElement.DeepEquals(Element(SchemaText), parsed.RootElement));
        }
        if (mode == VllmApiMode.AnthropicMessages) Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
    }

    private static void AssertMetadata(ChatResponse response, string requested)
    {
        Assert.NotNull(response.AdditionalProperties);
        Assert.Equal(requested, response.AdditionalProperties["vllm.structured_output.requested"]);
        Assert.Equal("json_object", response.AdditionalProperties["vllm.structured_output.actual"]);
        Assert.Equal(false, response.AdditionalProperties["vllm.structured_output.provider_schema_enforced"]);
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions, false)]
    [InlineData(VllmApiMode.ChatCompletions, true)]
    [InlineData(VllmApiMode.AnthropicMessages, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true)]
    [InlineData(VllmApiMode.Responses, false)]
    [InlineData(VllmApiMode.Responses, true)]
    public async Task ExistingSchemaInterface_EmitsOnlyJsonObject_AndPreservesCaller(VllmApiMode mode, bool stream)
    {
        using var handler = new Handler(mode, stream); using var http = new HttpClient(handler); using var client = Client(mode, http);
        var schema = Element(SchemaText); var format = ChatResponseFormat.ForJsonSchema(schema, "probe");
        var options = new VllmChatOptions { ThinkingEnabled = false, ResponseFormat = format, MaxOutputTokens = 256 };
        ChatMessage[] messages = [new(ChatRole.System, "original system one"), new(ChatRole.System, "original system two"), new(ChatRole.User, "answer")];
        var updates = new List<ChatResponseUpdate>();
        var response = await Invoke(client, messages, options, stream, updates);
        AssertWire(Assert.Single(handler.Bodies), mode, true);
        Assert.True(Element(response.Text).GetProperty("ok").GetBoolean());
        AssertMetadata(response, "json_schema");
        if (!stream)
        {
            Assert.Equal(10, response.Usage?.InputTokenCount);
            Assert.Equal(3, response.Usage?.OutputTokenCount);
            Assert.Equal(13, response.Usage?.TotalTokenCount);
        }
        else
        {
            // Existing non-Sonnet paths expose their custom UsageChatResponseUpdate;
            // JSON-format adaptation must preserve those updates, not invent usage or fix unrelated aggregation.
            using var baselineHandler = new Handler(mode, true); using var baselineHttp = new HttpClient(baselineHandler); using var baseline = Client(mode, baselineHttp, "other-model");
            var baselineUpdates = new List<ChatResponseUpdate>();
            var originalResponse = await Invoke(baseline, messages, options, true, baselineUpdates);
            var originalUsage = baselineUpdates.OfType<UsageChatResponseUpdate>().Select(x => (x.Usage?.InputTokenCount, x.Usage?.OutputTokenCount, x.Usage?.TotalTokenCount)).ToArray();
            Assert.NotEmpty(originalUsage);
            Assert.Equal(originalUsage, updates.OfType<UsageChatResponseUpdate>().Select(x => (x.Usage?.InputTokenCount, x.Usage?.OutputTokenCount, x.Usage?.TotalTokenCount)).ToArray());
            Assert.Equal(baselineUpdates.Select(x => x.FinishReason), updates.Select(x => x.FinishReason));
            Assert.Equal(baselineUpdates.SelectMany(x => x.Contents).Select(x => x.GetType()), updates.SelectMany(x => x.Contents).Select(x => x.GetType()));
            Assert.Equal(originalResponse.Usage?.InputTokenCount, response.Usage?.InputTokenCount);
            Assert.Equal(originalResponse.Text, response.Text);
        }
        if (stream) Assert.Single(updates.Where(x => x.AdditionalProperties?.ContainsKey("vllm.structured_output.actual") == true));
        Assert.Same(format, options.ResponseFormat); Assert.False(options.ThinkingEnabled);
        Assert.Equal(3, messages.Length); Assert.Equal("original system one", messages[0].Text); Assert.Equal("original system two", messages[1].Text);
        var system = SystemText(Element(handler.Bodies[0]), mode);
        Assert.True(system.IndexOf("original system one", StringComparison.Ordinal) < system.IndexOf("original system two", StringComparison.Ordinal));
        Assert.True(system.IndexOf("original system two", StringComparison.Ordinal) < system.IndexOf(Marker, StringComparison.Ordinal));
        Assert.Equal(SchemaText, schema.GetRawText());
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    [InlineData(VllmApiMode.Responses)]
    public async Task BareJsonAndModelOverride_UseObjectMode(VllmApiMode mode)
    {
        using var handler = new Handler(mode, false); using var http = new HttpClient(handler); using var client = Client(mode, http, "non-target-model");
        var options = new VllmChatOptions { ModelId = Model, ResponseFormat = ChatResponseFormat.Json, ThinkingEnabled = false, MaxOutputTokens = 256 };
        var response = await client.GetResponseAsync([new(ChatRole.User, "answer")], options);
        AssertWire(Assert.Single(handler.Bodies), mode, false); AssertMetadata(response, "json_object");
    }

    [Theory]
    [InlineData("{\"type\":\"array\"}")]
    [InlineData("{\"type\":\"string\"}")]
    [InlineData("{\"type\":[\"array\",\"string\"]}")]
    [InlineData("{\"const\":7}")]
    [InlineData("{\"enum\":[[],\"value\"]}")]
    [InlineData("{\"enum\":[]}")]
    [InlineData("false")]
    public async Task ImpossibleObjectRoots_RejectBeforeHttp(string schema)
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync([new(ChatRole.User, "answer")], new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(schema)) }));
        Assert.Empty(handler.Bodies);
    }

    [Theory]
    [InlineData("{\"type\":\"object\"}")]
    [InlineData("{}")]
    [InlineData("{\"type\":[\"object\",\"null\"]}")]
    [InlineData("{\"anyOf\":[{\"type\":\"object\"}]}")]
    [InlineData("{\"$ref\":\"#/$defs/root\",\"$defs\":{\"root\":{\"type\":\"object\"}}}")]
    [InlineData("true")]
    public async Task ObjectOrIndeterminateRoots_AreNotRejectedAsKnownIncompatible(string schema)
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        await client.GetResponseAsync([new(ChatRole.User, "answer")], new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(schema)), ThinkingEnabled = false });
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task ReusingAndConcurrentCalls_DoNotAccumulateDirectives()
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        ChatMessage[] messages = [new(ChatRole.User, "answer")];
        var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), ThinkingEnabled = false };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.GetResponseAsync(messages, options)));
        Assert.Equal(4, handler.Bodies.Count);
        foreach (var body in handler.Bodies) AssertWire(body, VllmApiMode.AnthropicMessages, true);
        Assert.Single(messages); Assert.Equal(ChatRole.User, messages[0].Role);
    }

    private sealed class Once(IEnumerable<ChatMessage> source) : IEnumerable<ChatMessage>
    {
        private int _count;
        public IEnumerator<ChatMessage> GetEnumerator()
        {
            Assert.Equal(1, Interlocked.Increment(ref _count)); return source.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task SingleUseInput_IsMaterializedWithoutMultipleEnumeration()
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        await client.GetResponseAsync(new Once([new(ChatRole.User, "answer")]), new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), ThinkingEnabled = false });
        AssertWire(Assert.Single(handler.Bodies), VllmApiMode.AnthropicMessages, true);
    }

    [Fact]
    public async Task NonTargetEffectiveModel_KeepsNativeSchemaAndHasNoFallbackMetadata()
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        var response = await client.GetResponseAsync([new(ChatRole.User, "answer")], new VllmChatOptions
        { ModelId = "deepseek-v4.1-flash", ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), ThinkingEnabled = false });
        var root = Element(Assert.Single(handler.Bodies));
        Assert.Equal("json_schema", root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.True(JsonElement.DeepEquals(Element(SchemaText), root.GetProperty("output_config").GetProperty("format").GetProperty("schema")));
        Assert.DoesNotContain(Marker, handler.Bodies[0]);
        Assert.False(response.AdditionalProperties?.ContainsKey("vllm.structured_output.actual") ?? false);
    }

    [Fact]
    public async Task ToolParameterSchema_IsIdenticalBeforeAndAfterResponseFormatAdaptation()
    {
        var tool = AIFunctionFactory.Create((string value) => value, "echo");
        using var originalHandler = new Handler(VllmApiMode.AnthropicMessages, false); using var originalHttp = new HttpClient(originalHandler); using var original = Client(VllmApiMode.AnthropicMessages, originalHttp, "other-model");
        using var targetHandler = new Handler(VllmApiMode.AnthropicMessages, false); using var targetHttp = new HttpClient(targetHandler); using var target = Client(VllmApiMode.AnthropicMessages, targetHttp);
        var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), ThinkingEnabled = false, Tools = [tool] };
        await original.GetResponseAsync([new(ChatRole.User, "answer")], options);
        await target.GetResponseAsync([new(ChatRole.User, "answer")], options);
        var before = Element(Assert.Single(originalHandler.Bodies)).GetProperty("tools");
        var after = Element(Assert.Single(targetHandler.Bodies)).GetProperty("tools");
        Assert.True(JsonElement.DeepEquals(before, after));
        Assert.Same(tool, Assert.Single(options.Tools!));
        Assert.Equal("object", after[0].GetProperty("input_schema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("format")]
    [InlineData("response_format")]
    [InlineData("structured_outputs")]
    [InlineData("output_config")]
    [InlineData("output_format")]
    [InlineData("text")]
    public async Task ExplicitFormattingOverride_RejectsWithoutMutatingCallerOrSending(string key)
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        var explicitValue = Element("""{"format":{"type":"json_schema","schema":{"type":"object"}}}""");
        var properties = new AdditionalPropertiesDictionary { [key] = explicitValue, ["caller_note"] = "preserve" };
        var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), AdditionalProperties = properties, ThinkingEnabled = false };
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync([new(ChatRole.User, "answer")], options));
        Assert.Empty(handler.Bodies); Assert.Same(properties, options.AdditionalProperties);
        Assert.Equal(2, properties.Count); Assert.Equal("preserve", properties["caller_note"]);
        Assert.True(JsonElement.DeepEquals(explicitValue, (JsonElement)properties[key]!));
    }

    [Fact]
    public async Task UnrelatedExtensionDictionary_RemainsUnmodifiedAndDoesNotBlockMapping()
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        var properties = new AdditionalPropertiesDictionary { ["caller_note"] = "preserve", ["caller_number"] = 7 };
        var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(SchemaText)), AdditionalProperties = properties, ThinkingEnabled = false };
        await client.GetResponseAsync([new(ChatRole.User, "answer")], options);
        AssertWire(Assert.Single(handler.Bodies), VllmApiMode.AnthropicMessages, true);
        Assert.Same(properties, options.AdditionalProperties); Assert.Equal(2, properties.Count);
        Assert.Equal("preserve", properties["caller_note"]); Assert.Equal(7, properties["caller_number"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"not a schema\"")]
    [InlineData("[]")]
    public async Task InvalidSchemaValues_ThrowArgumentExceptionBeforeHttp(string schema)
    {
        using var handler = new Handler(VllmApiMode.AnthropicMessages, false); using var http = new HttpClient(handler); using var client = Client(VllmApiMode.AnthropicMessages, http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync([new(ChatRole.User, "answer")], new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Element(schema)) }));
        Assert.Empty(handler.Bodies);
    }

    private sealed class GatedHandler(VllmApiMode mode, bool stream) : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = await request.Content!.ReadAsStringAsync(cancellationToken);
            Arrived.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(Reply(mode, stream), Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
        }
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions, false)]
    [InlineData(VllmApiMode.ChatCompletions, true)]
    [InlineData(VllmApiMode.AnthropicMessages, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true)]
    [InlineData(VllmApiMode.Responses, false)]
    [InlineData(VllmApiMode.Responses, true)]
    public async Task DisposingSchemaDocumentAfterRequestSerialization_DoesNotBreakResponseMetadata(VllmApiMode mode, bool stream)
    {
        using var handler = new GatedHandler(mode, stream); using var http = new HttpClient(handler); using var client = Client(mode, http);
        var document = JsonDocument.Parse(SchemaText);
        try
        {
            var options = new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement), ThinkingEnabled = false };
            var pending = Invoke(client, [new(ChatRole.User, "answer")], options, stream);
            await handler.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            document.Dispose();
            handler.Release.TrySetResult(true);
            AssertMetadata(await pending, "json_schema");
        }
        finally { document.Dispose(); handler.Release.TrySetResult(true); }
    }
}
