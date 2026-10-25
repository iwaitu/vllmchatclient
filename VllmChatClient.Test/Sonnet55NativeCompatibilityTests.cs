using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Pure-fake offline tests for the Claude Sonnet 5.5 native Anthropic Messages support: exact family boundary, thinking wire mapping, forced-tool rejection before any
/// request, and the versioned opaque <see cref="TextReasoningContent.ProtectedData"/> carrier for thinking / redacted_thinking blocks (non-streaming and streaming,
/// SDK response → host history → next SDK request). Every response comes from an in-memory <see cref="HttpMessageHandler"/>; nothing here touches a network,
/// a credential or a real endpoint.
/// </summary>
public class Sonnet55NativeCompatibilityTests
{
    private const string Sonnet = "claude-sonnet-5-5";
    private const string OldClaude = "claude-sonnet-4-5";

    private static string J(object value) => JsonSerializer.Serialize(value);

    private sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static (IChatClient Client, HttpClient Http, List<string> Bodies) Create(string model, Func<string> response, string contentType = "application/json")
    {
        var bodies = new List<string>();
        var http = new HttpClient(new Fake(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(), Encoding.UTF8, contentType) };
        }));
        IChatClient client = new VllmClaudeChatClient("https://offline.test/v1", "test-key", model, http, VllmApiMode.AnthropicMessages);
        return (client, http, bodies);
    }

    private static string PlainBody() => J(new
    {
        id = "msg_plain", type = "message", role = "assistant", model = Sonnet,
        content = new object[] { new { type = "text", text = "ok" } },
        stop_reason = "end_turn", usage = new { input_tokens = 1, output_tokens = 1 }
    });

    private static string Event(string kind, object value) => "event: " + kind + "\ndata: " + J(value) + "\n\n";

    private static string PlainStreamBody()
    {
        var sse = new StringBuilder();
        sse.Append(Event("message_start", new { type = "message_start", message = new { id = "msg_plain", type = "message", role = "assistant", model = Sonnet, content = Array.Empty<object>(), usage = new { input_tokens = 1, output_tokens = 0 } } }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }));
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "ok" } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sse.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn" }, usage = new { output_tokens = 1 } }));
        sse.Append(Event("message_stop", new { type = "message_stop" }));
        return sse.ToString();
    }

    private static List<AITool> EchoTool() => [AIFunctionFactory.Create(() => "x", "echo")];

    private static JsonElement[] AssistantBlocks(string body)
    {
        using var document = JsonDocument.Parse(body);
        var assistant = document.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant");
        var content = assistant.GetProperty("content");
        if (content.ValueKind == JsonValueKind.String)
        {
            // the request builder collapses a lone text block into a plain string content; read it as that one text block
            using var single = JsonDocument.Parse(J(new { type = "text", text = content.GetString() }));
            return [single.RootElement.Clone()];
        }

        return content.EnumerateArray().Select(b => b.Clone()).ToArray();
    }

    // ---- usage: raw numbers stay accountable, the standard input count includes the cache parts, nothing is added twice ----------------------------------

    private static string UsageStream(object startUsage, object deltaUsage)
    {
        var sse = new StringBuilder();
        sse.Append(Event("message_start", new { type = "message_start", message = new { id = "msg_u", type = "message", role = "assistant", model = Sonnet, content = Array.Empty<object>(), usage = startUsage } }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }));
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "ok" } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sse.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn" }, usage = deltaUsage }));
        sse.Append(Event("message_stop", new { type = "message_stop" }));
        return sse.ToString();
    }

    private static readonly object CachedStart = new
    {
        input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
        cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 1
    };

    private static void AssertCachedUsage(UsageDetails? usage)
    {
        Assert.NotNull(usage);
        Assert.Equal(19L, usage.InputTokenCount);                       // uncached 10 + cache read 5 + cache creation 4
        Assert.Equal(7L, usage.OutputTokenCount);                       // cumulative, not 1 + 7
        Assert.Equal(26L, usage.TotalTokenCount);
        var counts = usage.AdditionalCounts!;
        Assert.Equal(10L, counts["input_tokens"]);                      // raw uncached input stays available
        Assert.Equal(5L, counts["cache_read_input_tokens"]);
        Assert.Equal(4L, counts["cache_creation_input_tokens"]);
        Assert.Equal(3L, counts["cache_creation.ephemeral_5m_input_tokens"]);
        Assert.Equal(1L, counts["cache_creation.ephemeral_1h_input_tokens"]);
        Assert.Equal(7L, counts["output_tokens"]);
    }

    [Fact]
    public async Task StreamingUsageMergesStartInputAndCacheWithTheCumulativeDeltaOutput()
    {
        var (client, http, _) = Create(Sonnet, () => UsageStream(CachedStart, new { output_tokens = 7 }), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var usageUpdate = Assert.Single(updates.OfType<UsageChatResponseUpdate>());
            AssertCachedUsage(usageUpdate.Usage);
            Assert.Single(usageUpdate.Contents.OfType<UsageContent>());                                  // the standard carrier, exactly once
            Assert.Single(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
            AssertCachedUsage(updates.ToChatResponse().Usage);                                           // what a standard consumer (and QRE) aggregates
            Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
        }
    }

    [Fact]
    public async Task StreamingUsageUsesTheLatestCumulativeValuesWhenTheDeltaRepeatsInput()
    {
        var repeated = new
        {
            input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
            cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7
        };
        var (client, http, _) = Create(Sonnet, () => UsageStream(CachedStart, repeated), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            AssertCachedUsage(Assert.Single(updates.OfType<UsageChatResponseUpdate>()).Usage);   // no double counting
            Assert.Single(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
            AssertCachedUsage(updates.ToChatResponse().Usage);
        }
    }

    [Fact]
    public async Task StreamingUsageDerivesCacheCreationFromTheTtlDetailsWhenNoTotalIsGiven()
    {
        var start = new { input_tokens = 10, cache_read_input_tokens = 5, cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 1 };
        var (client, http, _) = Create(Sonnet, () => UsageStream(start, new { output_tokens = 7 }), "text/event-stream");
        using (http)
        using (client)
        {
            var usage = Assert.Single((await Collect(client)).OfType<UsageChatResponseUpdate>()).Usage!;
            Assert.Equal(19L, usage.InputTokenCount);
            Assert.Equal(7L, usage.OutputTokenCount);
            Assert.False(usage.AdditionalCounts!.ContainsKey("cache_creation_input_tokens"));
        }
    }

    [Fact]
    public async Task StreamingUsageWithOnlyTheStartObjectIsStillReportedOnce()
    {
        var (client, http, _) = Create(Sonnet, () => UsageStream(new { input_tokens = 10, output_tokens = 2 }, new { }), "text/event-stream");
        using (http)
        using (client)
        {
            var usage = Assert.Single((await Collect(client)).OfType<UsageChatResponseUpdate>()).Usage!;
            Assert.Equal(10L, usage.InputTokenCount);
            Assert.Equal(2L, usage.OutputTokenCount);
            Assert.Equal(12L, usage.TotalTokenCount);
        }
    }

    [Fact]
    public async Task NonStreamingUsageIncludesTheCacheParts()
    {
        var body = J(new
        {
            id = "msg_u", type = "message", role = "assistant", model = Sonnet,
            content = new object[] { new { type = "text", text = "ok" } },
            stop_reason = "end_turn",
            usage = new
            {
                input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
                cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7
            }
        });
        var (client, http, _) = Create(Sonnet, () => body);
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", Thinking);
            AssertCachedUsage(response.Usage);
        }
    }

    [Fact]
    public async Task OtherModelsKeepTheLegacyUsageMapping()
    {
        var body = J(new
        {
            id = "msg_u", type = "message", role = "assistant", model = OldClaude,
            content = new object[] { new { type = "text", text = "ok" } },
            stop_reason = "end_turn",
            usage = new { input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4, output_tokens = 7 }
        });
        var (client, http, _) = Create(OldClaude, () => body);
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", Thinking);
            Assert.Equal(10L, response.Usage!.InputTokenCount);          // unchanged: raw input only, no cache folding, no extra counts
            Assert.Equal(7L, response.Usage.OutputTokenCount);
            Assert.True(response.Usage.AdditionalCounts is null || response.Usage.AdditionalCounts.Count == 0);
        }
    }

    // ---- family boundary -------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("claude-sonnet-5.5", true)]
    [InlineData("Claude-Sonnet-5-5", true)]
    [InlineData(" anthropic/claude-sonnet-5-5 ", true)]
    [InlineData("claude-sonnet-5-5-20260101", true)]
    [InlineData("claude-sonnet-5.5-20260101", true)]
    [InlineData("claude-sonnet-5-5@20260101", true)]
    [InlineData("claude-sonnet-5-50", false)]
    [InlineData("claude-sonnet-5.50", false)]
    [InlineData("claude-sonnet-5-5x", false)]
    [InlineData("claude-sonnet-5-5-preview", false)]
    [InlineData("claude-sonnet-5-5-2026010", false)]
    [InlineData("claude-sonnet-5-5-202601011", false)]
    [InlineData("claude-sonnet-5", false)]
    [InlineData("claude-sonnet-4-5", false)]
    [InlineData("claude-opus-4.6", false)]
    [InlineData("deepseek-v4.1-flash-1017", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FamilyBoundaryIsStrict(string? model, bool expected)
    {
        Assert.Equal(expected, VllmClaudeSonnet55.IsFamily(model));
    }

    // ---- thinking wire mapping ------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Sonnet, false, false, "{\"type\":\"between_tools\"}")]
    [InlineData(Sonnet, true, false, "{\"type\":\"adaptive\"}")]
    [InlineData(Sonnet, false, true, "{\"type\":\"between_tools\"}")]
    [InlineData(Sonnet, true, true, "{\"type\":\"adaptive\"}")]
    [InlineData(OldClaude, false, false, "{\"type\":\"disabled\"}")]
    [InlineData(OldClaude, false, true, "{\"type\":\"disabled\"}")]
    public async Task ThinkingIsMappedForSonnetAndUnchangedForOtherModels(string model, bool thinkingEnabled, bool stream, string expectedThinking)
    {
        var (client, http, bodies) = Create(model, () => stream ? PlainStreamBody() : PlainBody(), stream ? "text/event-stream" : "application/json");
        using (http)
        using (client)
        {
            var options = new VllmChatOptions { ThinkingEnabled = thinkingEnabled, MaxOutputTokens = 256 };
            if (stream)
            {
                await foreach (var _ in client.GetStreamingResponseAsync("go", options))
                {
                }
            }
            else
            {
                await client.GetResponseAsync("go", options);
            }
        }

        using var document = JsonDocument.Parse(Assert.Single(bodies));
        Assert.Equal(expectedThinking, document.RootElement.GetProperty("thinking").GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SonnetWithPlainChatOptionsSendsNoThinkingObject(bool stream)
    {
        var (client, http, bodies) = Create(Sonnet, () => stream ? PlainStreamBody() : PlainBody(), stream ? "text/event-stream" : "application/json");
        using (http)
        using (client)
        {
            if (stream)
            {
                await foreach (var _ in client.GetStreamingResponseAsync("go", new ChatOptions { MaxOutputTokens = 256 }))
                {
                }
            }
            else
            {
                await client.GetResponseAsync("go", new ChatOptions { MaxOutputTokens = 256 });
            }
        }

        using var document = JsonDocument.Parse(Assert.Single(bodies));
        Assert.False(document.RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task OtherModelsKeepTheEnabledBudgetShape()
    {
        var (client, http, bodies) = Create(OldClaude, PlainBody);
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 8000 });
        }

        using var document = JsonDocument.Parse(Assert.Single(bodies));
        var thinking = document.RootElement.GetProperty("thinking");
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
        Assert.True(thinking.TryGetProperty("budget_tokens", out _));
    }

    // ---- forced tool choice ---------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<bool, bool> ForcedModes => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory]
    [MemberData(nameof(ForcedModes))]
    public async Task SonnetRejectsForcedToolsBeforeAnyRequest(bool specific, bool stream)
    {
        var (client, http, bodies) = Create(Sonnet, () => stream ? PlainStreamBody() : PlainBody(), stream ? "text/event-stream" : "application/json");
        using (http)
        using (client)
        {
            var options = new ChatOptions
            {
                Tools = EchoTool(),
                ToolMode = specific ? ChatToolMode.RequireSpecific("echo") : ChatToolMode.RequireAny,
            };

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                if (stream)
                {
                    await foreach (var _ in client.GetStreamingResponseAsync("go", options))
                    {
                    }
                }
                else
                {
                    await client.GetResponseAsync("go", options);
                }
            });
        }

        Assert.Empty(bodies);
    }

    [Theory]
    [InlineData(false, "auto")]
    [InlineData(true, "none")]
    public async Task SonnetSupportsAutoAndNone(bool none, string expectedType)
    {
        var (client, http, bodies) = Create(Sonnet, PlainBody);
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", new ChatOptions { Tools = EchoTool(), ToolMode = none ? ChatToolMode.None : ChatToolMode.Auto });
        }

        using var document = JsonDocument.Parse(Assert.Single(bodies));
        Assert.Equal(expectedType, document.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(false, "any")]
    [InlineData(true, "tool")]
    public async Task OtherModelsStillSendForcedTools(bool specific, string expectedType)
    {
        var (client, http, bodies) = Create(OldClaude, PlainBody);
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", new ChatOptions
            {
                Tools = EchoTool(),
                ToolMode = specific ? ChatToolMode.RequireSpecific("echo") : ChatToolMode.RequireAny,
            });
        }

        using var document = JsonDocument.Parse(Assert.Single(bodies));
        Assert.Equal(expectedType, document.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
    }

    // ---- fixtures: one assistant turn with signed + empty-text signed thinking, redacted_thinking, text and two tool calls --------------------------------

    private static readonly string ThinkingA = "{\"type\":\"thinking\",\"thinking\":\"think A\",\"signature\":\"SIG-A\",\"x_unknown\":{\"k\":[1,2]}}";
    private static readonly string ThinkingEmpty = "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"SIG-EMPTY\"}";
    private static readonly string Redacted = "{\"type\":\"redacted_thinking\",\"data\":\"RED-1\",\"x_extra\":{\"k\":[1,2]}}";

    private static string NonStreamBody() => "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"" + Sonnet + "\",\"content\":["
        + ThinkingA + "," + ThinkingEmpty + "," + Redacted + ","
        + "{\"type\":\"text\",\"text\":\"hello world\"},"
        + "{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"echo\",\"input\":{\"b\":2,\"a\":1}},"
        + "{\"type\":\"tool_use\",\"id\":\"toolu_2\",\"name\":\"echo\",\"input\":{\"z\":\"x\",\"a\":[1,2]}}],"
        + "\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":11,\"output_tokens\":7}}";

    private static string StreamBody()
    {
        var sse = new StringBuilder();
        sse.Append(Event("message_start", new { type = "message_start", message = new { id = "msg_s", type = "message", role = "assistant", model = "claude-sonnet-5-5-20260101", content = Array.Empty<object>(), usage = new { input_tokens = 11, output_tokens = 0 } } }));
        sse.Append("event: ping\ndata: {\"type\":\"ping\"}\n\n");
        sse.Append("event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"\",\"x_unknown\":{\"k\":[1,2]}}}\n\n");
        foreach (var chunk in new[] { "think ", "A" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = chunk } }));
        }

        foreach (var chunk in new[] { "SIG-", "A" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "signature_delta", signature = chunk } }));
        }

        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));

        sse.Append("event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\"}}\n\n");
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "signature_delta", signature = "SIG-EMPTY" } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 1 }));

        sse.Append("event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":2,\"content_block\":" + Redacted + "}\n\n");
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 2 }));

        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 3, content_block = new { type = "text", text = "" } }));
        foreach (var chunk in new[] { "hello ", "world" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 3, delta = new { type = "text_delta", text = chunk } }));
        }

        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 3 }));

        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 4, content_block = new { type = "tool_use", id = "toolu_1", name = "echo", input = new { } } }));
        foreach (var chunk in new[] { "{\"b\":2,", "\"a\":1}" })
        {
            sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 4, delta = new { type = "input_json_delta", partial_json = chunk } }));
        }

        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 4 }));

        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 5, content_block = new { type = "tool_use", id = "toolu_2", name = "echo", input = new { } } }));
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 5, delta = new { type = "input_json_delta", partial_json = "{\"z\":\"x\",\"a\":[1,2]}" } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 5 }));

        sse.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "tool_use" }, usage = new { output_tokens = 9 } }));
        sse.Append(Event("message_stop", new { type = "message_stop" }));
        return sse.ToString();
    }

    private static readonly string[] ExpectedOpaqueStream =
    [
        "{\"type\":\"thinking\",\"thinking\":\"think A\",\"signature\":\"SIG-A\",\"x_unknown\":{\"k\":[1,2]}}",
        "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"SIG-EMPTY\"}",
        "{\"type\":\"redacted_thinking\",\"data\":\"RED-1\",\"x_extra\":{\"k\":[1,2]}}",
    ];

    private static void AssertReplayedBlocks(JsonElement[] blocks)
    {
        Assert.Equal(6, blocks.Length);
        Assert.Equal(ExpectedOpaqueStream[0], blocks[0].GetRawText());
        Assert.Equal(ExpectedOpaqueStream[1], blocks[1].GetRawText());
        Assert.Equal(ExpectedOpaqueStream[2], blocks[2].GetRawText());
        Assert.Equal("text", blocks[3].GetProperty("type").GetString());
        Assert.Equal("hello world", blocks[3].GetProperty("text").GetString());
        Assert.Equal("tool_use", blocks[4].GetProperty("type").GetString());
        Assert.Equal("toolu_1", blocks[4].GetProperty("id").GetString());
        Assert.Equal("echo", blocks[4].GetProperty("name").GetString());
        Assert.Equal("{\"b\":2,\"a\":1}", blocks[4].GetProperty("input").GetRawText());
        Assert.Equal("toolu_2", blocks[5].GetProperty("id").GetString());
        Assert.Equal("{\"z\":\"x\",\"a\":[1,2]}", blocks[5].GetProperty("input").GetRawText());
        Assert.DoesNotContain(blocks, b => b.GetProperty("type").GetString() == "thinking" && b.GetProperty("signature").GetString()!.Length == 0);
    }

    private static List<ChatMessage> HistoryWithToolResults(IEnumerable<ChatMessage> assistantMessages)
    {
        var history = new List<ChatMessage> { new(ChatRole.User, "go") };
        history.AddRange(assistantMessages);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_1", "{\"ok\":1}"), new FunctionResultContent("toolu_2", "{\"ok\":2}")]));
        return history;
    }

    private static readonly VllmChatOptions Thinking = new() { ThinkingEnabled = true, MaxOutputTokens = 4096 };

    private static void AssertNoLegacyReasoningCopies(IEnumerable<AIContent> contents)
    {
        foreach (var call in contents.OfType<FunctionCallContent>())
        {
            Assert.True(call.AdditionalProperties is null || !call.AdditionalProperties.ContainsKey("anthropic_thinking"));
            Assert.True(call.AdditionalProperties is null || !call.AdditionalProperties.ContainsKey("reasoning_content"));
        }
    }

    // ---- non-streaming ----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NonStreamingBlocksAreCarriedOpaqueAndReplayedByteForByteInOrder()
    {
        var first = true;
        var (client, http, bodies) = Create(Sonnet, () => first ? NonStreamBody() : PlainBody());
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", Thinking);

            var contents = response.Messages.SelectMany(m => m.Contents).ToList();
            AssertNoLegacyReasoningCopies(contents);
            Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
            Assert.Equal(11L, response.Usage!.InputTokenCount);
            Assert.Equal(7L, response.Usage.OutputTokenCount);

            var opaque = contents.OfType<TextReasoningContent>().Where(r => r.ProtectedData is not null).ToList();
            Assert.Equal(3, opaque.Count);
            Assert.All(opaque, r => Assert.Equal(string.Empty, r.Text));
            var display = contents.OfType<TextReasoningContent>().Where(r => r.ProtectedData is null).ToList();
            Assert.Equal("think A", Assert.Single(display).Text);                                  // display text exactly once, empty-text block adds none
            Assert.Equal(2, contents.OfType<FunctionCallContent>().Count());
            Assert.True(response.Messages.All(m => m.AdditionalProperties is null || !m.AdditionalProperties.ContainsKey("anthropic_content_blocks")));

            first = false;
            await client.GetResponseAsync(HistoryWithToolResults(response.Messages), Thinking);
        }

        AssertReplayedBlocks(AssistantBlocks(bodies[^1]));
    }

    [Fact]
    public async Task NonStreamingWithoutThinkingStillRoundTripsTextAndTools()
    {
        var first = true;
        var body = J(new
        {
            id = "msg_2", type = "message", role = "assistant", model = Sonnet,
            content = new object[] { new { type = "text", text = "calling" }, new { type = "tool_use", id = "toolu_1", name = "echo", input = new { v = "q" } } },
            stop_reason = "tool_use", usage = new { input_tokens = 2, output_tokens = 3 }
        });
        var (client, http, bodies) = Create(Sonnet, () => first ? body : PlainBody());
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", Thinking);
            Assert.DoesNotContain(response.Messages.SelectMany(m => m.Contents), c => c is TextReasoningContent);
            first = false;
            var history = new List<ChatMessage> { new(ChatRole.User, "go") };
            history.AddRange(response.Messages);
            history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_1", "{}")]));
            await client.GetResponseAsync(history, Thinking);
        }

        var blocks = AssistantBlocks(bodies[^1]);
        Assert.Equal(new[] { "text", "tool_use" }, blocks.Select(b => b.GetProperty("type").GetString()).ToArray());
        Assert.DoesNotContain(blocks, b => b.GetProperty("type").GetString() == "thinking");
    }

    [Fact]
    public async Task NonStreamingUnknownBlockTypeIsRejectedNotDropped()
    {
        var body = J(new
        {
            id = "msg_3", type = "message", role = "assistant", model = Sonnet,
            content = new object[] { new { type = "server_tool_use", id = "srv_1", name = "web_search", input = new { } }, new { type = "text", text = "x" } },
            stop_reason = "end_turn", usage = new { input_tokens = 1, output_tokens = 1 }
        });
        var (client, http, _) = Create(Sonnet, () => body);
        using (http)
        using (client)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync("go", Thinking));
        }
    }

    [Fact]
    public async Task NonStreamingThinkingWithoutSignatureIsRejected()
    {
        var body = J(new
        {
            id = "msg_4", type = "message", role = "assistant", model = Sonnet,
            content = new object[] { new { type = "thinking", thinking = "no signature" } },
            stop_reason = "end_turn", usage = new { input_tokens = 1, output_tokens = 1 }
        });
        var (client, http, _) = Create(Sonnet, () => body);
        using (http)
        using (client)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync("go", Thinking));
        }
    }

    // ---- streaming --------------------------------------------------------------------------------------------------------------------------------------

    private static async Task<List<ChatResponseUpdate>> Collect(IChatClient client, string prompt = "go")
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(prompt, Thinking))
        {
            updates.Add(update);
        }

        return updates;
    }

    [Fact]
    public async Task StreamingEmitsDisplayOnceAndOneOpaqueItemPerBlockInIndexOrder()
    {
        var (client, http, _) = Create(Sonnet, StreamBody, "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var contents = updates.SelectMany(u => u.Contents).ToList();

            var opaque = contents.OfType<TextReasoningContent>().Where(r => r.ProtectedData is not null).ToList();
            Assert.Equal(3, opaque.Count);
            Assert.All(opaque, r => Assert.Equal(string.Empty, r.Text));
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(VllmClaudeSonnet55.OpaquePrefix + ExpectedOpaqueStream[i], opaque[i].ProtectedData);
            }

            var display = contents.OfType<TextReasoningContent>().Where(r => r.ProtectedData is null).ToList();
            Assert.Equal("think A", string.Concat(display.Select(r => r.Text)));                    // thinking text is delivered exactly once
            Assert.DoesNotContain(contents.OfType<TextContent>(), t => t.Text.Contains("think", StringComparison.Ordinal));
            Assert.Equal("hello world", string.Concat(contents.OfType<TextContent>().Select(t => t.Text)));
            AssertNoLegacyReasoningCopies(contents);

            var calls = contents.OfType<FunctionCallContent>().ToList();
            Assert.Equal(new[] { "toolu_1", "toolu_2" }, calls.Select(c => c.CallId).ToArray());
            Assert.Equal(new[] { "b", "a" }, calls[0].Arguments!.Keys.ToArray());                   // argument order is kept

            Assert.Single(contents.OfType<UsageContent>());
            var streamUsage = Assert.Single(updates.OfType<UsageChatResponseUpdate>()).Usage!;
            Assert.Equal(11L, streamUsage.InputTokenCount);                                          // message_start input is kept
            Assert.Equal(9L, streamUsage.OutputTokenCount);
            Assert.Equal(20L, streamUsage.TotalTokenCount);
            Assert.Equal(ChatFinishReason.ToolCalls, updates[^1].FinishReason);
            Assert.All(updates.Where(u => u.FinishReason is not null), u => Assert.Equal(ChatFinishReason.ToolCalls, u.FinishReason));
            Assert.Equal(1, updates.Count(u => u.FinishReason is not null));
        }
    }

    [Fact]
    public async Task StreamingAggregatedByToChatResponseIsReplayedFaithfully()
    {
        var first = true;
        var (client, http, bodies) = Create(Sonnet, () => first ? StreamBody() : PlainStreamBody(), "text/event-stream");
        using (http)
        using (client)
        {
            var aggregated = (await Collect(client)).ToChatResponse();
            Assert.Equal(11L, aggregated.Usage!.InputTokenCount);                                        // message_start input survives the standard aggregation
            Assert.Equal(9L, aggregated.Usage.OutputTokenCount);
            Assert.Equal(20L, aggregated.Usage.TotalTokenCount);
            first = false;
            await foreach (var _ in client.GetStreamingResponseAsync(HistoryWithToolResults(aggregated.Messages), Thinking))
            {
            }
        }

        AssertReplayedBlocks(AssistantBlocks(bodies[^1]));
    }

    [Fact]
    public async Task StreamingUncoalescedItemsLikeTheQreHistoryAreReplayedFaithfully()
    {
        var first = true;
        var (client, http, bodies) = Create(Sonnet, () => first ? StreamBody() : PlainStreamBody(), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            first = false;
            // QRE keeps every delta as its own item and rebuilds fresh messages (no RawRepresentation / AdditionalProperties).
            var rebuilt = new List<AIContent>();
            foreach (var content in updates.SelectMany(u => u.Contents))
            {
                rebuilt.Add(content switch
                {
                    TextContent text => new TextContent(text.Text),
                    TextReasoningContent reasoning => new TextReasoningContent(reasoning.Text) { ProtectedData = reasoning.ProtectedData },
                    FunctionCallContent call => new FunctionCallContent(call.CallId, call.Name, call.Arguments),
                    _ => content,
                });
            }

            await foreach (var _ in client.GetStreamingResponseAsync(HistoryWithToolResults([new ChatMessage(ChatRole.Assistant, rebuilt)]), Thinking))
            {
            }
        }

        AssertReplayedBlocks(AssistantBlocks(bodies[^1]));
    }

    [Fact]
    public async Task StreamingUnknownBlockTypeAndUnknownDeltaAreRejected()
    {
        var unknownBlock = Event("message_start", new { type = "message_start", message = new { id = "m", model = Sonnet } })
            + Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "server_tool_use", id = "s", name = "web_search" } });
        var unknownDelta = Event("message_start", new { type = "message_start", message = new { id = "m", model = Sonnet } })
            + Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } })
            + Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "citations_delta", citation = new { } } });

        foreach (var body in new[] { unknownBlock, unknownDelta })
        {
            var (client, http, _) = Create(Sonnet, () => body, "text/event-stream");
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => Collect(client));
            }
        }
    }

    [Fact]
    public async Task StreamingTruncatedBlockAndErrorEventFailInsteadOfDroppingData()
    {
        var truncated = Event("message_start", new { type = "message_start", message = new { id = "m", model = Sonnet } })
            + Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "" } })
            + Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = "partial" } });
        var error = Event("message_start", new { type = "message_start", message = new { id = "m", model = Sonnet } })
            + Event("error", new { type = "error", error = new { type = "overloaded_error", message = "x" } });

        foreach (var body in new[] { truncated, error })
        {
            var (client, http, _) = Create(Sonnet, () => body, "text/event-stream");
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(client));
            }
        }
    }

    [Fact]
    public async Task StreamingEmptyBodyYieldsOnlyTheLeadingEmptyUpdate()
    {
        var (client, http, _) = Create(Sonnet, () => string.Empty, "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var only = Assert.Single(updates);
            Assert.Empty(only.Contents);
            Assert.Null(only.FinishReason);
        }
    }

    [Fact]
    public async Task StreamingCancellationIsObserved()
    {
        var (client, http, _) = Create(Sonnet, StreamBody, "text/event-stream");
        using (http)
        using (client)
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in client.GetStreamingResponseAsync("go", Thinking, cts.Token))
                {
                }
            });
        }
    }

    // ---- outbound validation of the opaque carrier ---------------------------------------------------------------------------------------------------

    private static async Task<(Exception? Error, List<string> Bodies)> SendHistory(string model, params AIContent[] assistantContents)
    {
        var (client, http, bodies) = Create(model, PlainBody);
        using (http)
        using (client)
        {
            var history = new List<ChatMessage> { new(ChatRole.User, "go"), new(ChatRole.Assistant, assistantContents.ToList()), new(ChatRole.User, "next") };
            try
            {
                await client.GetResponseAsync(history, Thinking);
                return (null, bodies);
            }
            catch (Exception ex)
            {
                return (ex, bodies);
            }
        }
    }

    [Theory]
    [InlineData("vllm.anthropic.block.v1:{\"type\":\"thinking\",\"thinking\":\"x\",\"signature\":\"S")]               // truncated JSON
    [InlineData("vllm.anthropic.block.v1:{\"type\":\"thinking\",\"thinking\":\"x\"}")]                                 // no signature
    [InlineData("vllm.anthropic.block.v1:{\"type\":\"redacted_thinking\"}")]                                           // no data
    [InlineData("vllm.anthropic.block.v1:{\"type\":\"text\",\"text\":\"x\"}")]                                         // not a reasoning block
    [InlineData("vllm.anthropic.block.v1:[1,2]")]                                                                      // not an object
    [InlineData("vllm.anthropic.block.v2:{\"type\":\"thinking\",\"thinking\":\"x\",\"signature\":\"S\"}")]             // unknown version
    [InlineData("vllm.anthropic.block.v1")]                                                                            // prefix without separator
    public async Task InvalidOpaquePayloadsAreRejectedBeforeAnyRequest(string payload)
    {
        var (error, bodies) = await SendHistory(Sonnet, new TextReasoningContent(string.Empty) { ProtectedData = payload }, new TextContent("hi"));

        Assert.IsType<NotSupportedException>(error);
        Assert.DoesNotContain("SIG", error!.Message, StringComparison.Ordinal);
        Assert.Empty(bodies);
    }

    [Fact]
    public async Task ForeignProtectedDataIsRejectedForSonnetButIgnoredForOtherModels()
    {
        var foreign = new TextReasoningContent("r") { ProtectedData = "encrypted-by-another-provider" };

        var (sonnetError, sonnetBodies) = await SendHistory(Sonnet, foreign, new TextContent("hi"));
        Assert.IsType<NotSupportedException>(sonnetError);
        Assert.Empty(sonnetBodies);

        var (oldError, oldBodies) = await SendHistory(OldClaude, foreign, new TextContent("hi"));
        Assert.Null(oldError);
        Assert.Single(oldBodies);
    }

    [Fact]
    public async Task DisplayOnlyReasoningItemsAreNeverRebuiltAsThinking()
    {
        var (error, bodies) = await SendHistory(Sonnet, new TextReasoningContent("display only"), new TextContent("hi"));
        Assert.Null(error);
        var blocks = AssistantBlocks(Assert.Single(bodies));
        Assert.DoesNotContain(blocks, b => b.GetProperty("type").GetString() is "thinking" or "redacted_thinking");
        Assert.DoesNotContain("display only", Assert.Single(bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisplayItemsBesideOpaqueItemsDoNotAddThinkingBlocks()
    {
        var opaque = new TextReasoningContent(string.Empty) { ProtectedData = VllmClaudeSonnet55.OpaquePrefix + ThinkingA };
        var (error, bodies) = await SendHistory(Sonnet, new TextReasoningContent("think A"), opaque, new TextContent("hi"));
        Assert.Null(error);
        var blocks = AssistantBlocks(Assert.Single(bodies));
        Assert.Equal(2, blocks.Length);
        Assert.Equal(ThinkingA, blocks[0].GetRawText());
        Assert.Equal("hi", blocks[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task UnicodeThinkingTextSurvivesTheCarrierValueForValue()
    {
        var block = J(new { type = "thinking", thinking = "思考 ✓ \"q\" \\ é", signature = "SIG-U" });
        var opaque = new TextReasoningContent(string.Empty) { ProtectedData = VllmClaudeSonnet55.OpaquePrefix + block };
        var (error, bodies) = await SendHistory(Sonnet, opaque, new TextContent("hi"));
        Assert.Null(error);
        var replayed = AssistantBlocks(Assert.Single(bodies))[0];
        Assert.Equal("思考 ✓ \"q\" \\ é", replayed.GetProperty("thinking").GetString());
        Assert.Equal("SIG-U", replayed.GetProperty("signature").GetString());
    }

    // ---- default behaviour of other models is unchanged ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OtherModelsKeepTheLegacyReplayCopiesAndNoOpaqueItems()
    {
        var body = J(new
        {
            id = "msg_old", type = "message", role = "assistant", model = OldClaude,
            content = new object[]
            {
                new { type = "thinking", thinking = "old think", signature = "OLD-SIG" },
                new { type = "tool_use", id = "toolu_1", name = "echo", input = new { v = 1 } }
            },
            stop_reason = "tool_use", usage = new { input_tokens = 1, output_tokens = 1 }
        });
        var (client, http, _) = Create(OldClaude, () => body);
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", Thinking);
            var contents = response.Messages.SelectMany(m => m.Contents).ToList();
            Assert.DoesNotContain(contents, c => c is TextReasoningContent);
            Assert.Contains(response.Messages, m => m.AdditionalProperties is not null && m.AdditionalProperties.ContainsKey("anthropic_content_blocks"));
            var call = Assert.Single(contents.OfType<FunctionCallContent>());
            Assert.Equal("old think", call.AdditionalProperties!["anthropic_thinking"]);
        }
    }
}
