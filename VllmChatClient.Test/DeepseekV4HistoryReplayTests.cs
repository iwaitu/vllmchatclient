using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Offline regression for the DeepSeek V4 history replay (2.0.30). A response is received through the real SDK, its <c>response.Messages</c> are handed back
/// unchanged by the "host", and the next outbound request is captured. The display filter of 2.0.29 already cleaned what the caller sees; these tests
/// assert that the replayed history sent to the model carries no audit tag either, while the V3 default family, the caller's raw objects, the user/system
/// content, tool arguments/results, opaque signed thinking and every other block stay exactly as they were. Responses mode is intentionally not covered
/// (unverified, not claimed).
/// </summary>
public class DeepseekV4HistoryReplayTests
{
    private const string Model = "deepseek-v4.1-flash-1017";
    private const string Tag = "<ds_safety>hidden</ds_safety>Safe\n\n";
    private const string Literal = "<ds_safety>keep-in-argument</ds_safety>";

    private static string J(object value) => JsonSerializer.Serialize(value);
    private static JsonElement El(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static HttpResponseMessage Reply(string body, string type = "application/json")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    private static (IChatClient Client, HttpClient Http, List<string> Bodies) Create(bool v4, bool native, Func<string> response, string contentType = "application/json")
    {
        var bodies = new List<string>();
        var http = new HttpClient(new Fake(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Reply(response(), contentType);
        }));
        var mode = native ? VllmApiMode.AnthropicMessages : VllmApiMode.ChatCompletions;
        var endpoint = native ? "https://offline.test/apps/anthropic" : "https://offline.test/v1";
        IChatClient client = v4
            ? new VllmDeepseekV4ChatClient(endpoint, "test-key", Model, http, mode)
            : new VllmDeepseekV3ChatClient(endpoint, "test-key", Model, http, mode);
        return (client, http, bodies);
    }

    private static int Count(string text, string needle = "ds_safety")
    {
        var count = 0;
        for (var index = text.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = text.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static readonly int LiteralTags = Count(Literal);

    private static readonly VllmChatOptions ThinkingOn = new() { ThinkingEnabled = true, MaxOutputTokens = 256 };

    // ---- fixtures: one assistant turn with reasoning, answer text and a tool call whose argument is tag-shaped ----------------------------------------

    private static string NonStreamBody(bool native, string text = "answer", string reasoning = "think T1 <ds_safety>hidden</ds_safety>Safe\n\nT2")
        => native
            ? J(new
            {
                id = "msg_1", type = "message", role = "assistant", model = Model,
                content = new object[]
                {
                    new { type = "thinking", thinking = reasoning, signature = "SIG-IGNORED-BY-SDK" },
                    new { type = "text", text },
                    new { type = "tool_use", id = "toolu_1", name = "echo", input = new { literal = Literal } }
                },
                stop_reason = "tool_use", usage = new { input_tokens = 11, output_tokens = 7 }
            })
            : J(new
            {
                id = "chat_1", @object = "chat.completion", created = 1, model = Model,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new
                        {
                            role = "assistant", content = text, reasoning_content = reasoning,
                            tool_calls = new[] { new { id = "call_1", type = "function", function = new { name = "echo", arguments = J(new { literal = Literal }) } } }
                        },
                        finish_reason = "tool_calls"
                    }
                },
                usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 }
            });

    private static string Event(string kind, object value) => "event: " + kind + "\ndata: " + J(value) + "\n\n";

    private static string NativeStreamBody()
    {
        var sse = new StringBuilder();
        sse.Append(Event("message_start", new { type = "message_start", message = new { id = "m", type = "message", role = "assistant", model = Model, content = Array.Empty<object>(), usage = new { input_tokens = 1, output_tokens = 0 } } }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "" } }));
        foreach (var chunk in new[] { "think T1 <ds_", "safety>hidden</ds_safety>Sa", "fe\n\nT2" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = chunk } }));
        }
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 1, content_block = new { type = "text", text = "" } }));
        foreach (var chunk in new[] { "answer A1 <", "ds_safety>hidden</ds_", "safety>Sa", "fe\n\nA2" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "text_delta", text = chunk } }));
        }
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 1 }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 2, content_block = new { type = "tool_use", id = "toolu_1", name = "echo", input = new { } } }));
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 2, delta = new { type = "input_json_delta", partial_json = J(new { literal = Literal }) } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 2 }));
        sse.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "tool_use" }, usage = new { output_tokens = 7 } }));
        sse.Append(Event("message_stop", new { type = "message_stop" }));
        return sse.ToString();
    }

    private static string ChatChunk(object delta, string? finish = null)
        => "data: " + J(new { id = "c1", @object = "chat.completion.chunk", created = 1, model = Model, choices = new[] { new { index = 0, delta, finish_reason = finish } } }) + "\n\n";

    private static string ChatStreamBody()
    {
        var sse = new StringBuilder();
        foreach (var chunk in new[] { "think T1 <ds_", "safety>hidden</ds_safety>Sa", "fe\n\nT2" })
        {
            sse.Append(ChatChunk(new { reasoning_content = chunk }));
        }
        foreach (var chunk in new[] { "answer A1 <ds_", "safety>hidden</ds_safety>Sa", "fe\n\nA2" })
        {
            sse.Append(ChatChunk(new { content = chunk }));
        }
        sse.Append(ChatChunk(new { tool_calls = new[] { new { index = 0, id = "call_1", type = "function", function = new { name = "echo", arguments = J(new { literal = Literal }) } } } }));
        sse.Append(ChatChunk(new { }, "tool_calls"));
        sse.Append("data: [DONE]\n\n");
        return sse.ToString();
    }

    /// <summary>First request through the real SDK, then the host hands <c>response.Messages</c> back unchanged with a tool result; returns the second request body.</summary>
    private static async Task<(string Replay, ChatResponse First)> RoundTrip(bool v4, bool native, bool stream)
    {
        var first = true;
        var (client, http, bodies) = Create(v4, native, () => first
                ? (stream ? (native ? NativeStreamBody() : ChatStreamBody()) : NonStreamBody(native))
                : (stream ? "data: [DONE]\n\n" : NonStreamBody(native, "ok", "r")),
            stream ? "text/event-stream" : "application/json");
        using (http)
        using (client)
        {
            ChatResponse firstResponse;
            if (stream)
            {
                var updates = new List<ChatResponseUpdate>();
                await foreach (var update in client.GetStreamingResponseAsync("go", ThinkingOn))
                {
                    updates.Add(update);
                }
                firstResponse = updates.ToChatResponse();
            }
            else
            {
                firstResponse = await client.GetResponseAsync("go", ThinkingOn);
            }

            var call = firstResponse.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
            Assert.Equal(Literal, call.Arguments!["literal"]?.ToString());
            var history = new List<ChatMessage> { new(ChatRole.User, "go") };
            history.AddRange(firstResponse.Messages);                                   // unchanged, exactly as the host received them
            history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "{\"ok\":true}")]));
            first = false;
            if (stream)
            {
                await foreach (var _ in client.GetStreamingResponseAsync(history, ThinkingOn))
                {
                }
            }
            else
            {
                await client.GetResponseAsync(history, ThinkingOn);
            }

            return (bodies[^1], firstResponse);
        }
    }

    private static string ReplayedToolArgument(string replay, bool native)
    {
        using var document = JsonDocument.Parse(replay);
        var messages = document.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        return native
            ? messages.SelectMany(m => m.GetProperty("content").EnumerateArray()).Single(c => c.GetProperty("type").GetString() == "tool_use").GetProperty("input").GetProperty("literal").GetString()!
            : JsonDocument.Parse(messages.Single(m => m.TryGetProperty("tool_calls", out _)).GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString()!)
                .RootElement.GetProperty("literal").GetString()!;
    }

    // ---- the four required paths: real SDK response.Messages handed back unchanged, hard assert clean -------------------------------------------------

    [Theory]
    [InlineData(false, false)]   // chat, non-streaming
    [InlineData(false, true)]    // chat, streaming
    [InlineData(true, false)]    // anthropic, non-streaming
    [InlineData(true, true)]     // anthropic, streaming
    public async Task V4_ReplayedHistoryCarriesNoAuditTagExceptTheToolArgumentLiteral(bool native, bool stream)
    {
        var (replay, first) = await RoundTrip(v4: true, native, stream);

        Assert.Equal(Literal, ReplayedToolArgument(replay, native));
        Assert.Equal(LiteralTags, Count(replay));                       // the only audit-tag text left is the tool-argument literal
        Assert.Contains("T1", replay, StringComparison.Ordinal);        // the reasoning text itself is kept (only the tag block is removed)
        Assert.Contains("T2", replay, StringComparison.Ordinal);
        Assert.DoesNotContain("T1 <", replay, StringComparison.Ordinal);
        if (!stream)
        {
            Assert.Contains("answer", replay, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("A1", replay, StringComparison.Ordinal);
            Assert.Contains("A2", replay, StringComparison.Ordinal);
        }
        Assert.Contains(first.Messages, m => m.Contents.OfType<FunctionCallContent>().Any());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task V3_ControlReplayKeepsTheOriginalTagsSoTheDefaultFamilyIsUnchanged(bool native, bool stream)
    {
        var (replay, _) = await RoundTrip(v4: false, native, stream);

        Assert.Equal(Literal, ReplayedToolArgument(replay, native));
        Assert.True(Count(replay) > LiteralTags);                      // V3 replays the tag text it received
    }

    /// <summary>
    /// Inherited boundary, characterised and NOT fixed here: for a streamed Anthropic turn the base adapter puts the reasoning text in front of the answer inside
    /// the replayed <c>text</c> block (the same text block V3 replays). V4 must only differ by the removed tag blocks: the thinking block is exactly the cleaned
    /// reasoning, the text block is exactly the cleaned reasoning followed by the cleaned answer, and V3 shows the same layout with the original tags.
    /// </summary>
    [Fact]
    public async Task NativeStreamingReplayKeepsTheInheritedReasoningInTextBlockMappingAndOnlyRemovesTheTagBlocks()
    {
        static (string Thinking, string Text) Blocks(string replay)
        {
            using var document = JsonDocument.Parse(replay);
            var content = document.RootElement.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("role").GetString() == "assistant").GetProperty("content").EnumerateArray().ToArray();
            return (content.Single(c => c.GetProperty("type").GetString() == "thinking").GetProperty("thinking").GetString()!,
                    content.Single(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!);
        }

        var (v4, _) = await RoundTrip(v4: true, native: true, stream: true);
        var (v3, _) = await RoundTrip(v4: false, native: true, stream: true);

        var (v4Thinking, v4Text) = Blocks(v4);
        Assert.Equal("think T1 T2", v4Thinking);
        Assert.Equal("think T1 T2answer A1 A2", v4Text);
        var (v3Thinking, v3Text) = Blocks(v3);
        Assert.Equal("think T1 " + Tag + "T2", v3Thinking);
        Assert.Equal("think T1 " + Tag + "T2answer A1 " + Tag + "A2", v3Text);
    }

    [Fact]
    public async Task NonStreamingRawObjectsKeepTheOriginalTextAfterTheCleanedRequest()
    {
        var (_, chat) = await RoundTrip(v4: true, native: false, stream: false);
        var chatRaw = Assert.IsType<VllmChatResponseMessage>(chat.Messages[0].RawRepresentation);
        var reasoning = Assert.IsType<JsonElement>(chatRaw.ReasoningContent);          // the object the 2.0.29 `is string` test could never see
        Assert.Equal(JsonValueKind.String, reasoning.ValueKind);
        Assert.Contains("<ds_safety>", reasoning.GetString(), StringComparison.Ordinal);

        var (_, native) = await RoundTrip(v4: true, native: true, stream: false);
        var raw = Assert.IsType<VllmAnthropicMessagesResponse>(native.Messages[0].RawRepresentation);
        Assert.Contains(raw.Content!, b => b.Type == "thinking" && b.Thinking!.Contains("<ds_safety>", StringComparison.Ordinal));
        var stored = Assert.IsType<JsonElement[]>(native.Messages[0].AdditionalProperties!["anthropic_content_blocks"]);
        Assert.Contains(stored, block => block.GetRawText().Contains("ds_safety", StringComparison.Ordinal));      // the caller-owned replay blocks were not rewritten
        Assert.Equal("answer", native.Text);                                           // the display text was already clean in 2.0.29
    }

    // ---- typed chat reasoning sources (caller-built history) ------------------------------------------------------------------------------------------

    private static async Task<string> ChatReplay(bool v4, object? reasoning, string content = "answer")
    {
        var (client, http, bodies) = Create(v4, native: false, () => NonStreamBody(false, "ok", "r"));
        using (http)
        using (client)
        {
            var assistant = new ChatMessage(ChatRole.Assistant, [new TextContent(content), new FunctionCallContent("call_1", "echo", new Dictionary<string, object?> { ["literal"] = Literal })]);
            if (reasoning is not null)
            {
                assistant.AdditionalProperties = new AdditionalPropertiesDictionary { ["reasoning_content"] = reasoning };
            }
            var history = new List<ChatMessage> { new(ChatRole.User, "go"), assistant, new(ChatRole.Tool, [new FunctionResultContent("call_1", "{}")]) };
            await client.GetResponseAsync(history, ThinkingOn);
            return bodies[^1];
        }
    }

    [Fact]
    public async Task ChatReasoning_StringAndJsonElementStringAreBothCleanedAndOtherJsonTypesAreLeftAlone()
    {
        var tagged = "R1 " + Tag + "R2";
        foreach (var source in new object[] { tagged, El(tagged) })
        {
            var replay = await ChatReplay(v4: true, source);
            using var document = JsonDocument.Parse(replay);
            var assistant = document.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.TryGetProperty("tool_calls", out _));
            Assert.Equal("R1 R2", assistant.GetProperty("reasoning_content").GetString());
            Assert.Equal(LiteralTags, Count(replay));
            Assert.True(Count(await ChatReplay(v4: false, source)) > LiteralTags);          // V3 control keeps the tag
        }

        var array = El(new[] { "keep " + Tag + "as a JSON array" });
        var arrayReplay = await ChatReplay(v4: true, array);
        using (var document = JsonDocument.Parse(arrayReplay))
        {
            var reasoningElement = document.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.TryGetProperty("tool_calls", out _)).GetProperty("reasoning_content");
            Assert.Equal(JsonValueKind.Array, reasoningElement.ValueKind);          // not stringified, not cleaned: unknown JSON types are kept as they are
            Assert.Contains("ds_safety", reasoningElement.GetRawText(), StringComparison.Ordinal);
        }
    }

    // ---- native: new request DTO only, exact blocks, exact bytes --------------------------------------------------------------------------------------

    private static async Task<string> NativeReplay(bool v4, JsonElement[] assistantBlocks, params ChatMessage[] extraBefore)
    {
        var (client, http, bodies) = Create(v4, native: true, () => NonStreamBody(true, "ok", "r"));
        using (http)
        using (client)
        {
            var assistant = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("toolu_1", "echo", new Dictionary<string, object?> { ["literal"] = Literal })])
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { ["anthropic_content_blocks"] = assistantBlocks },
            };
            var history = new List<ChatMessage>(extraBefore) { new(ChatRole.User, "go"), assistant, new(ChatRole.Tool, [new FunctionResultContent("toolu_1", "{}")]) };
            await client.GetResponseAsync(history, ThinkingOn);
            return bodies[^1];
        }
    }

    private static JsonElement[] Blocks(bool tagged, string signature = "")
    {
        var thinking = "思考 😀 " + (tagged ? Tag : string.Empty) + "结束";
        var text = "回答 🎉 \"引号\" é " + (tagged ? Tag : string.Empty) + "完毕";
        var thinkingBlock = signature.Length == 0 ? El(new { type = "thinking", thinking }) : El(new { type = "thinking", thinking, signature });
        return [thinkingBlock, El(new { type = "text", text, citations = new[] { new { n = 1.50m, ok = true } } }), El(new { type = "tool_use", id = "toolu_1", name = "echo", input = new { literal = Literal, ratio = 0.10m } })];
    }

    [Fact]
    public async Task NativeTaggedHistoryIsByteIdenticalToTheV3RequestForTheAlreadyCleanHistory()
    {
        // V4 on the tagged history must produce exactly what V3 produces for the same history written without the tag block: Chinese, emoji, escapes,
        // unknown properties and number formats of every block are untouched.
        var v4 = await NativeReplay(v4: true, Blocks(tagged: true));
        var v3Clean = await NativeReplay(v4: false, Blocks(tagged: false));

        Assert.Equal(v3Clean, v4);
        Assert.Equal(LiteralTags, Count(v4));
        using var sent = JsonDocument.Parse(v4);
        var content = sent.RootElement.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("role").GetString() == "assistant").GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("思考 😀 结束", content[0].GetProperty("thinking").GetString());
        Assert.Equal("回答 🎉 \"引号\" é 完毕", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task NativeHistoryWithoutAnyTagIsNotRewrittenAndMatchesV3Bytes()
    {
        var v4 = await NativeReplay(v4: true, Blocks(tagged: false));
        var v3 = await NativeReplay(v4: false, Blocks(tagged: false));

        Assert.Equal(v3, v4);
        Assert.Equal(LiteralTags, Count(v4));
    }

    [Fact]
    public async Task ChatHistoryWithoutAnyTagMatchesV3BytesIncludingChineseAndEmoji()
    {
        var v4 = await ChatReplay(v4: true, "思考 😀 \"q\" é", "回答 🎉 完毕");
        var v3 = await ChatReplay(v4: false, "思考 😀 \"q\" é", "回答 🎉 完毕");

        Assert.Equal(v3, v4);
    }

    [Fact]
    public async Task SignedThinkingIsAnOpaqueObjectWhileAdjacentTextIsStillCleaned()
    {
        var blocks = Blocks(tagged: true, signature: "OPAQUE-SIG-123");

        var v4 = await NativeReplay(v4: true, blocks);
        var v3 = await NativeReplay(v4: false, blocks);

        using var sent = JsonDocument.Parse(v4);
        var content = sent.RootElement.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("role").GetString() == "assistant").GetProperty("content").EnumerateArray().ToArray();
        var thinking = content[0];
        Assert.Equal("OPAQUE-SIG-123", thinking.GetProperty("signature").GetString());
        Assert.Contains("<ds_safety>", thinking.GetProperty("thinking").GetString(), StringComparison.Ordinal);      // not cleaned: a signed block is sent exactly as given
        Assert.DoesNotContain("ds_safety", content[1].GetProperty("text").GetString(), StringComparison.Ordinal);   // the adjacent unsigned text block is cleaned
        Assert.Equal("回答 🎉 \"引号\" é 完毕", content[1].GetProperty("text").GetString());
        Assert.Equal(Literal, content[2].GetProperty("input").GetProperty("literal").GetString());
        Assert.True(Count(v3) > Count(v4));                                                                          // and V3 still sends the text tag
    }

    [Fact]
    public async Task RedactedThinkingUnknownBlocksAndUnknownPropertiesAreKeptVerbatim()
    {
        JsonElement[] blocks =
        [
            El(new { type = "redacted_thinking", data = "REDACTED " + Tag }),
            El(new { type = "x_custom", text = "custom " + Tag, extra = new { a = 1, b = new[] { 1, 2 } } }),
            El(new { type = "text", text = "t " + Tag + "u", citations = new[] { new { n = 1 } }, cache_control = new { type = "ephemeral" } }),
            El(new { type = "tool_use", id = "toolu_1", name = "echo", input = new { literal = Literal } }),
        ];

        var v4 = await NativeReplay(v4: true, blocks);

        using var sent = JsonDocument.Parse(v4);
        var content = sent.RootElement.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("role").GetString() == "assistant").GetProperty("content").EnumerateArray().ToArray();
        Assert.True(JsonElement.DeepEquals(blocks[0], content[0]));                  // redacted_thinking: untouched even though its data holds tag text
        Assert.True(JsonElement.DeepEquals(blocks[1], content[1]));                  // unknown block type: untouched, including its text and nested extras
        Assert.Contains("ds_safety", content[0].GetProperty("data").GetString(), StringComparison.Ordinal);
        Assert.Contains("ds_safety", content[1].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, content[1].GetProperty("extra").GetProperty("b").GetArrayLength());
        Assert.Equal("t u", content[2].GetProperty("text").GetString());
        Assert.Equal(1, content[2].GetProperty("citations")[0].GetProperty("n").GetInt32());                         // unknown properties of a cleaned block survive
        Assert.Equal("ephemeral", content[2].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal(new[] { "type", "text", "citations", "cache_control" }, content[2].EnumerateObject().Select(p => p.Name).ToArray());   // order preserved
        Assert.Equal(Literal, content[3].GetProperty("input").GetProperty("literal").GetString());
    }

    [Fact]
    public async Task UserSystemToolResultAndOtherMessagesAreNeverTouchedAndMultipleAssistantTurnsAreAllCleaned()
    {
        var (client, http, bodies) = Create(v4: true, native: true, () => NonStreamBody(true, "ok", "r"));
        using (http)
        using (client)
        {
            ChatMessage Assistant(string id) => new(ChatRole.Assistant, [new FunctionCallContent(id, "echo", new Dictionary<string, object?> { ["literal"] = Literal })])
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["anthropic_content_blocks"] = new[] { El(new { type = "thinking", thinking = "T " + id + Tag + "end" }), El(new { type = "text", text = "X " + id + Tag + "end" }),
                                                          El(new { type = "tool_use", id, name = "echo", input = new { literal = Literal } }) },
                },
            };
            var history = new List<ChatMessage>
            {
                new(ChatRole.System, "system mentions " + Literal),
                new(ChatRole.User, "user quotes " + Literal),
                Assistant("toolu_1"),
                new(ChatRole.Tool, [new FunctionResultContent("toolu_1", "result " + Literal)]),
                Assistant("toolu_2"),
                new(ChatRole.Tool, [new FunctionResultContent("toolu_2", "result " + Literal)]),
                new(ChatRole.User, "again " + Literal),
            };

            await client.GetResponseAsync(history, ThinkingOn);
            var body = bodies[^1];

            using var sent = JsonDocument.Parse(body);
            var messages = sent.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            var assistants = messages.Where(m => m.GetProperty("role").GetString() == "assistant").ToArray();
            Assert.Equal(2, assistants.Length);
            foreach (var assistant in assistants)
            {
                var content = assistant.GetProperty("content").EnumerateArray().ToArray();
                Assert.DoesNotContain("ds_safety", content[0].GetProperty("thinking").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain("ds_safety", content[1].GetProperty("text").GetString(), StringComparison.Ordinal);
                Assert.Equal(Literal, content[2].GetProperty("input").GetProperty("literal").GetString());
            }
            // Every other literal is still there exactly once: system (1) + user (2) + tool results (2) + tool_use inputs (2).
            Assert.EndsWith(Literal, sent.RootElement.GetProperty("system")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Equal(7 * LiteralTags, Count(body));
        }
    }

    [Fact]
    public async Task ReplayDoesNotMutateTheCallersMessagesOrStoredBlocks()
    {
        var blocks = Blocks(tagged: true);
        var before = blocks.Select(b => b.GetRawText()).ToArray();
        var assistant = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("toolu_1", "echo", new Dictionary<string, object?> { ["literal"] = Literal })])
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["anthropic_content_blocks"] = blocks },
        };
        var (client, http, _) = Create(v4: true, native: true, () => NonStreamBody(true, "ok", "r"));
        using (http)
        using (client)
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go"), assistant, new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_1", "{}")])], ThinkingOn);
        }

        Assert.Same(blocks, assistant.AdditionalProperties["anthropic_content_blocks"]);
        Assert.Equal(before, blocks.Select(b => b.GetRawText()).ToArray());
        Assert.Contains(blocks, b => b.GetRawText().Contains("ds_safety", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoTagNativeStreamingAndNonStreamingResponsesStillRoundTripTheSameAsV3()
    {
        string Body(bool native) => NonStreamBody(native, "clean", "clean reasoning");
        foreach (var native in new[] { false, true })
        {
            async Task<string> Replay(bool v4)
            {
                var first = true;
                var (client, http, bodies) = Create(v4, native, () => first ? Body(native) : NonStreamBody(native, "ok", "r"));
                using (http)
                using (client)
                {
                    var response = await client.GetResponseAsync("go", ThinkingOn);
                    var call = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
                    var history = new List<ChatMessage> { new(ChatRole.User, "go") };
                    history.AddRange(response.Messages);
                    history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "{}")]));
                    first = false;
                    await client.GetResponseAsync(history, ThinkingOn);
                    return bodies[^1];
                }
            }

            Assert.Equal(await Replay(v4: false), await Replay(v4: true));
        }
    }
}
