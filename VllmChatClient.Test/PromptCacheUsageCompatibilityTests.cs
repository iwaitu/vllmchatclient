using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Pure-fake offline tests for SDK 2.0.33: the explicit Anthropic prompt-cache opt-in (<see cref="VllmAnthropicCacheControl"/>) and the unified provider usage
/// metering across Chat Completions, Responses and Anthropic Messages (streaming and non-streaming). Every response comes from an in-memory
/// <see cref="HttpMessageHandler"/> through the public <see cref="IChatClient"/> API of the real client classes; nothing touches a network, a credential or a
/// real endpoint.
/// </summary>
public class PromptCacheUsageCompatibilityTests
{
    private const string Haiku = "claude-haiku-5-5";
    private const string Sonnet = "claude-sonnet-5-5";
    private const string DeepSeek1017 = "deepseek-v4.1-flash-1017";
    private const string InvalidUsageMessage = "Invalid provider usage counters.";

    private static string J(object? value) => JsonSerializer.Serialize(value);

    // ---- fake transport ----------------------------------------------------------------------------------------------------------------------------------

    private sealed class Wire
    {
        public List<string> Bodies { get; } = [];

        public int Requests => Bodies.Count;

        public JsonDocument Last() => JsonDocument.Parse(Bodies[^1]);
    }

    private sealed class Fake(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class BaseClient(HttpClient http, VllmApiMode mode) : VllmBaseChatClient("https://offline.test/v1", token: null, modelId: "test-model", httpClient: http, apiMode: mode);

    private static (IChatClient Client, HttpClient Http, Wire Wire) Make(Func<HttpClient, IChatClient> factory, Func<int, string> responder, string contentType = "application/json")
    {
        var wire = new Wire();
        var http = new HttpClient(new Fake(async request =>
        {
            int slot;
            lock (wire.Bodies)
            {
                wire.Bodies.Add(string.Empty);
                slot = wire.Bodies.Count - 1;
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            int number;
            lock (wire.Bodies)
            {
                wire.Bodies[slot] = body;
                number = slot + 1;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responder(number), Encoding.UTF8, contentType) };
        }));
        return (factory(http), http, wire);
    }

    private static Func<HttpClient, IChatClient> Claude(string model, VllmApiMode mode)
        => http => new VllmClaudeChatClient("https://offline.test/v1", "test-key", model, http, mode);

    private static Func<HttpClient, IChatClient> DeepSeek(VllmApiMode mode)
        => http => new VllmDeepseekV4ChatClient("https://offline.test/v1", "test-key", DeepSeek1017, http, mode);

    private static Func<HttpClient, IChatClient> Generic(VllmApiMode mode) => http => new BaseClient(http, mode);

    private static Func<HttpClient, IChatClient> AnthropicFlavor(string flavor) => flavor switch
    {
        "haiku" => Claude(Haiku, VllmApiMode.AnthropicMessages),
        "sonnet" => Claude(Sonnet, VllmApiMode.AnthropicMessages),
        "deepseek" => DeepSeek(VllmApiMode.AnthropicMessages),
        _ => throw new ArgumentOutOfRangeException(nameof(flavor)),
    };

    private static string ModelOf(string flavor) => flavor switch { "haiku" => Haiku, "sonnet" => Sonnet, _ => DeepSeek1017 };

    private static async Task<List<ChatResponseUpdate>> Collect(IChatClient client, ChatOptions? options = null)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync("go", options))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static ChatOptions WithCache(object? value) => new() { AdditionalProperties = new AdditionalPropertiesDictionary { [VllmAnthropicCacheControl.OptionKey] = value } };

    // ---- fixtures ------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly object CachedUsage = new
    {
        input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
        cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7
    };

    private static string AnthropicBody(string model, object? usage) => J(new
    {
        id = "msg_1", type = "message", role = "assistant", model,
        content = new object[] { new { type = "text", text = "ok" } },
        stop_reason = "end_turn", usage
    });

    private static string Event(string kind, object value) => "event: " + kind + "\ndata: " + J(value) + "\n\n";

    private static string AnthropicStream(string model, object? startUsage, object? deltaUsage, bool stop = true)
    {
        var sse = new StringBuilder();
        sse.Append(Event("message_start", new { type = "message_start", message = new { id = "msg_s", type = "message", role = "assistant", model, content = Array.Empty<object>(), usage = startUsage } }));
        sse.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }));
        sse.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "ok" } }));
        sse.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sse.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn" }, usage = deltaUsage }));
        if (stop)
        {
            sse.Append(Event("message_stop", new { type = "message_stop" }));
        }

        return sse.ToString();
    }

    private static void AssertCachedAnthropicUsage(UsageDetails? usage)
    {
        Assert.NotNull(usage);
        Assert.Equal(19L, usage.InputTokenCount);                       // uncached 10 + cache read 5 + cache creation 4
        Assert.Equal(7L, usage.OutputTokenCount);
        Assert.Equal(26L, usage.TotalTokenCount);
        var counts = usage.AdditionalCounts!;
        Assert.Equal(10L, counts["input_tokens"]);                      // raw uncached input stays available
        Assert.Equal(5L, counts["cache_read_input_tokens"]);
        Assert.Equal(4L, counts["cache_creation_input_tokens"]);
        Assert.Equal(3L, counts["cache_creation.ephemeral_5m_input_tokens"]);
        Assert.Equal(1L, counts["cache_creation.ephemeral_1h_input_tokens"]);
        Assert.Equal(7L, counts["output_tokens"]);
    }

    // ---- prompt cache configuration -----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    [InlineData("deepseek")]
    public async Task WithoutTheKeyTheRequestShapeIsUnchanged(string flavor)
    {
        var (client, http, wire) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), new { input_tokens = 1, output_tokens = 1 }));
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go");
            using var body = wire.Last();
            Assert.False(body.RootElement.TryGetProperty("cache_control", out _));
            Assert.DoesNotContain("anthropic.cache_control", wire.Bodies[0]);
        }
    }

    [Theory]
    [InlineData("haiku", null)]
    [InlineData("haiku", "5m")]
    [InlineData("haiku", "1h")]
    [InlineData("sonnet", "5m")]
    [InlineData("sonnet", "1h")]
    [InlineData("deepseek", "5m")]
    public async Task ExplicitConfigurationIsCopiedToTheTopLevelCacheControl(string flavor, string? ttl)
    {
        var (client, http, wire) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), new { input_tokens = 1, output_tokens = 1 }));
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", WithCache(new VllmAnthropicCacheControl(ttl)));
            using var body = wire.Last();
            var cacheControl = body.RootElement.GetProperty("cache_control");
            Assert.Equal("ephemeral", cacheControl.GetProperty("type").GetString());
            if (ttl is null)
            {
                Assert.False(cacheControl.TryGetProperty("ttl", out _));
            }
            else
            {
                Assert.Equal(ttl, cacheControl.GetProperty("ttl").GetString());
            }

            Assert.Equal(1, wire.Requests);
            Assert.DoesNotContain("anthropic.cache_control", wire.Bodies[0]);                 // the option key itself is never sent
        }
    }

    [Fact]
    public async Task StreamingRequestsCarryTheSameCacheControl()
    {
        var (client, http, wire) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), _ => AnthropicStream(Haiku, new { input_tokens = 1, output_tokens = 0 }, new { output_tokens = 1 }), "text/event-stream");
        using (http)
        using (client)
        {
            await Collect(client, WithCache(new VllmAnthropicCacheControl("1h")));
            using var body = wire.Last();
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("1h", body.RootElement.GetProperty("cache_control").GetProperty("ttl").GetString());
        }
    }

    [Fact]
    public async Task ANullValueSwitchesCachingOffAndSendsNothing()
    {
        var (client, http, wire) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), _ => AnthropicBody(Haiku, new { input_tokens = 1, output_tokens = 1 }));
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", WithCache(null));
            using var body = wire.Last();
            Assert.False(body.RootElement.TryGetProperty("cache_control", out _));
        }
    }

    [Theory]
    [InlineData("5M")]
    [InlineData("10m")]
    [InlineData("")]
    [InlineData("ephemeral")]
    public void TheTtlIsValidatedWhenTheSettingIsCreated(string ttl)
    {
        Assert.Throws<ArgumentException>(() => new VllmAnthropicCacheControl(ttl));
    }

    [Fact]
    public void TheSettingIsImmutableWithAFixedType()
    {
        var setting = new VllmAnthropicCacheControl("5m");
        Assert.Equal("ephemeral", setting.Type);
        Assert.Equal("5m", setting.Ttl);
        Assert.Null(new VllmAnthropicCacheControl().Ttl);
        Assert.Equal("anthropic.cache_control", VllmAnthropicCacheControl.OptionKey);
        Assert.Null(typeof(VllmAnthropicCacheControl).GetProperty(nameof(VllmAnthropicCacheControl.Ttl))!.SetMethod);
        Assert.Null(typeof(VllmAnthropicCacheControl).GetProperty(nameof(VllmAnthropicCacheControl.Type))!.SetMethod);
    }

    [Fact]
    public async Task OnlyTheTypedSettingIsAcceptedAndOtherValuesFailBeforeAnyRequest()
    {
        using var element = JsonDocument.Parse("""{"type":"ephemeral"}""");
        object?[] invalid = ["ephemeral", new { type = "ephemeral" }, element.RootElement.Clone(), new Dictionary<string, object?> { ["type"] = "ephemeral" }, 5, true];
        foreach (var value in invalid)
        {
            var (client, http, wire) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), _ => AnthropicBody(Haiku, null));
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync("go", WithCache(value)));
                await Assert.ThrowsAsync<ArgumentException>(async () => { await foreach (var _ in client.GetStreamingResponseAsync("go", WithCache(value))) { } });
                Assert.Equal(0, wire.Requests);
            }
        }
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("chat-stream")]
    [InlineData("claude-chat")]
    [InlineData("responses")]
    [InlineData("responses-stream")]
    public async Task ANonNullSettingWithAnotherApiModeIsRejectedBeforeAnyRequest(string scenario)
    {
        Func<HttpClient, IChatClient> factory = scenario switch
        {
            "chat" or "chat-stream" => Generic(VllmApiMode.ChatCompletions),
            "claude-chat" => Claude(Haiku, VllmApiMode.ChatCompletions),
            _ => Generic(VllmApiMode.Responses),
        };
        var (client, http, wire) = Make(factory, _ => "{}");
        using (http)
        using (client)
        {
            var options = WithCache(new VllmAnthropicCacheControl("5m"));
            if (scenario.EndsWith("-stream", StringComparison.Ordinal))
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => { await foreach (var _ in client.GetStreamingResponseAsync("go", options)) { } });
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync("go", options));
            }

            Assert.Equal(0, wire.Requests);
        }
    }

    [Fact]
    public async Task ANullSettingIsHarmlessForOtherApiModes()
    {
        var body = J(new { id = "c", created = 1, model = "m", choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = "ok" } } }, usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 } });
        var (client, http, wire) = Make(Generic(VllmApiMode.ChatCompletions), _ => body);
        using (http)
        using (client)
        {
            var response = await client.GetResponseAsync("go", WithCache(null));
            Assert.Equal("ok", response.Text);
            Assert.Equal(1, wire.Requests);
            Assert.DoesNotContain("cache_control", wire.Bodies[0]);
        }
    }

    [Fact]
    public async Task TheCallersOptionsAndHistoryAreNotModifiedAndOneSettingServesConcurrentCalls()
    {
        var setting = new VllmAnthropicCacheControl("1h");
        var options = WithCache(setting);
        options.AdditionalProperties!["unrelated"] = "keep";
        var history = new List<ChatMessage> { new(ChatRole.System, "be brief"), new(ChatRole.User, "go") };
        var before = (options.AdditionalProperties.Count, history.Count, history[0].Text, history[1].Text);

        var (client, http, wire) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), _ => AnthropicBody(Haiku, new { input_tokens = 1, output_tokens = 1 }));
        using (http)
        using (client)
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetResponseAsync(history, options)));

            Assert.Equal(8, wire.Requests);
            foreach (var body in wire.Bodies)
            {
                using var document = JsonDocument.Parse(body);
                Assert.Equal("1h", document.RootElement.GetProperty("cache_control").GetProperty("ttl").GetString());
            }

            Assert.Same(setting, options.AdditionalProperties[VllmAnthropicCacheControl.OptionKey]);
            Assert.Equal("keep", options.AdditionalProperties["unrelated"]);
            Assert.Equal(before, (options.AdditionalProperties.Count, history.Count, history[0].Text, history[1].Text));
        }
    }

    [Fact]
    public async Task CacheControlComposesWithTheDeepSeekJsonObjectMapping()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""");
        var options = WithCache(new VllmAnthropicCacheControl("5m"));
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement.Clone());
        var (client, http, wire) = Make(DeepSeek(VllmApiMode.AnthropicMessages), _ => AnthropicBody(DeepSeek1017, new { input_tokens = 1, output_tokens = 1 }));
        using (http)
        using (client)
        {
            await client.GetResponseAsync("go", options);
            using var body = wire.Last();
            Assert.Equal("json_object", body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
            Assert.Equal("5m", body.RootElement.GetProperty("cache_control").GetProperty("ttl").GetString());
        }
    }

    // ---- Anthropic usage: non-streaming ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    [InlineData("deepseek")]
    public async Task AnthropicNonStreamingUsageFoldsTheCachePartsAndKeepsEveryRawCount(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), CachedUsage));
        using (http)
        using (client)
        {
            AssertCachedAnthropicUsage((await client.GetResponseAsync("go")).Usage);
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task ARequestWithoutCachingKeepsTheOrdinaryCountsAndCreatesNoCacheKeys(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), new { input_tokens = 10, output_tokens = 7 }));
        using (http)
        using (client)
        {
            var usage = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(10L, usage.InputTokenCount);
            Assert.Equal(7L, usage.OutputTokenCount);
            Assert.Equal(17L, usage.TotalTokenCount);
            Assert.Equal(["input_tokens", "output_tokens"], usage.AdditionalCounts!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task ExplicitZeroCacheCountsAreKeptNotTreatedAsMissing(string flavor)
    {
        var usageJson = new { input_tokens = 10, cache_read_input_tokens = 0, cache_creation_input_tokens = 0, output_tokens = 7 };
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), usageJson));
        using (http)
        using (client)
        {
            var usage = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(10L, usage.InputTokenCount);
            Assert.Equal(0L, usage.AdditionalCounts!["cache_read_input_tokens"]);
            Assert.Equal(0L, usage.AdditionalCounts["cache_creation_input_tokens"]);
        }
    }

    [Theory]
    [InlineData("haiku", """{"output_tokens":7}""", null, 7L, null)]
    [InlineData("haiku", """{"cache_read_input_tokens":5}""", null, null, null)]
    [InlineData("haiku", """{"cache_read_input_tokens":5,"output_tokens":7}""", null, 7L, null)]
    [InlineData("haiku", """{"input_tokens":10}""", 10L, null, null)]
    [InlineData("haiku", """{"input_tokens":0,"output_tokens":0}""", 0L, 0L, 0L)]
    [InlineData("sonnet", """{"output_tokens":7}""", null, 7L, null)]
    [InlineData("sonnet", """{"cache_read_input_tokens":5}""", null, null, null)]
    [InlineData("sonnet", """{"input_tokens":10}""", 10L, null, null)]
    [InlineData("sonnet", """{"input_tokens":0,"output_tokens":0}""", 0L, 0L, 0L)]
    public async Task MissingCountersStayUnknownAndNeverBecomeZero(string flavor, string usageJson, long? input, long? output, long? total)
    {
        using var usageDocument = JsonDocument.Parse(usageJson);
        var body = AnthropicBody(ModelOf(flavor), usageDocument.RootElement.Clone());
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => body);
        using (http)
        using (client)
        {
            var usage = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(input, usage.InputTokenCount);
            Assert.Equal(output, usage.OutputTokenCount);
            Assert.Equal(total, usage.TotalTokenCount);
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task AResponseWithoutUsageHasNoUsageNotZero(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), null));
        using (http)
        using (client)
        {
            Assert.Null((await client.GetResponseAsync("go")).Usage);
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task TheCreationTotalIsDerivedFromTheTtlPartsOnlyWhenNoTotalIsGivenAndNeverAddedTwice(string flavor)
    {
        var onlyParts = new { input_tokens = 10, cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7 };
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), onlyParts));
        using (http)
        using (client)
        {
            var usage = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(14L, usage.InputTokenCount);                                        // 10 + (3 + 1)
            Assert.False(usage.AdditionalCounts!.ContainsKey("cache_creation_input_tokens")); // derived, not a raw field
            Assert.Equal(3L, usage.AdditionalCounts["cache_creation.ephemeral_5m_input_tokens"]);
            Assert.Equal(1L, usage.AdditionalCounts["cache_creation.ephemeral_1h_input_tokens"]);
        }

        var both = new { input_tokens = 10, cache_creation_input_tokens = 4, cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7 };
        var (client2, http2, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), both));
        using (http2)
        using (client2)
        {
            Assert.Equal(14L, (await client2.GetResponseAsync("go")).Usage!.InputTokenCount);  // the total wins, the parts are not added on top
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task CountersAboveInt32AreKeptAsLong(string flavor)
    {
        var big = new { input_tokens = 3_000_000_000L, cache_read_input_tokens = 2_500_000_000L, output_tokens = 1 };
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicBody(ModelOf(flavor), big));
        using (http)
        using (client)
        {
            var usage = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(5_500_000_000L, usage.InputTokenCount);
            Assert.Equal(5_500_000_001L, usage.TotalTokenCount);
        }
    }

    [Theory]
    [InlineData("haiku", """{"input_tokens":-1,"output_tokens":1}""")]
    [InlineData("haiku", """{"input_tokens":1.5,"output_tokens":1}""")]
    [InlineData("haiku", """{"input_tokens":"7","output_tokens":1}""")]
    [InlineData("haiku", """{"input_tokens":99999999999999999999,"output_tokens":1}""")]
    [InlineData("haiku", """{"input_tokens":9223372036854775807,"cache_read_input_tokens":1,"output_tokens":1}""")]
    [InlineData("sonnet", """{"input_tokens":-1,"output_tokens":1}""")]
    [InlineData("sonnet", """{"input_tokens":1.5,"output_tokens":1}""")]
    [InlineData("sonnet", """{"input_tokens":99999999999999999999,"output_tokens":1}""")]
    [InlineData("sonnet", """{"input_tokens":9223372036854775807,"cache_read_input_tokens":1,"output_tokens":1}""")]
    public async Task InvalidCountersRaiseTheFixedProtocolErrorWithoutTheirValues(string flavor, string usageJson)
    {
        var body = AnthropicBody(ModelOf(flavor), JsonDocument.Parse(usageJson).RootElement.Clone());
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => body);
        using (http)
        using (client)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("go"));
            Assert.Equal(InvalidUsageMessage, error.Message);
        }
    }

    // ---- Anthropic usage: streaming -----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    [InlineData("deepseek")]
    public async Task AnthropicStreamingMergesStartAndDeltaIntoOneUsageBeforeFinish(string flavor)
    {
        var start = new
        {
            input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
            cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 1
        };
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), start, new { output_tokens = 7 }), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var usageUpdate = Assert.Single(updates.OfType<UsageChatResponseUpdate>());
            AssertCachedAnthropicUsage(usageUpdate.Usage);
            var carrier = Assert.Single(usageUpdate.Contents.OfType<UsageContent>());
            Assert.Same(usageUpdate.Usage, carrier.Details);                                         // the same details, exactly one standard carrier
            Assert.Single(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
            AssertCachedAnthropicUsage(updates.ToChatResponse().Usage);                              // standard aggregation does not double
            Assert.True(updates.IndexOf(usageUpdate) < updates.FindIndex(u => u.FinishReason.HasValue));
            Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
            Assert.Equal("ok", string.Concat(updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text)));
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task ARepeatedCumulativeSnapshotUsesTheLatestValuesAndAPartialUpdateClearsNothing(string flavor)
    {
        var repeated = new
        {
            input_tokens = 10, cache_read_input_tokens = 5, cache_creation_input_tokens = 4,
            cache_creation = new { ephemeral_5m_input_tokens = 3, ephemeral_1h_input_tokens = 1 }, output_tokens = 7
        };
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), CachedUsage, repeated), "text/event-stream");
        using (http)
        using (client)
        {
            AssertCachedAnthropicUsage(Assert.Single((await Collect(client)).OfType<UsageChatResponseUpdate>()).Usage);
        }

        var (client2, http2, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), CachedUsage, new { }), "text/event-stream");
        using (http2)
        using (client2)
        {
            var usage = Assert.Single((await Collect(client2)).OfType<UsageChatResponseUpdate>()).Usage!;
            Assert.Equal(19L, usage.InputTokenCount);                                       // nothing from the start was cleared by the empty delta
            Assert.Equal(7L, usage.OutputTokenCount);                                       // start carried output_tokens = 7 in CachedUsage
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task AStreamEndingWithoutMessageStopStillEmitsFinishButNoUsage(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), CachedUsage, new { output_tokens = 7 }, stop: false), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
            Assert.Empty(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
            Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);                  // the known finish is still the last update
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task AStreamWithoutUsageEmitsNoUsageUpdate(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), null, null), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
            Assert.Null(updates.ToChatResponse().Usage);
        }
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    public async Task InvalidStreamingCountersAbortTheStreamAndNoUsageIsEmittedAfterwards(string flavor)
    {
        var (client, http, _) = Make(AnthropicFlavor(flavor), _ => AnthropicStream(ModelOf(flavor), new { input_tokens = 1 }, new { output_tokens = -3 }), "text/event-stream");
        using (http)
        using (client)
        {
            var seen = new List<ChatResponseUpdate>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var update in client.GetStreamingResponseAsync("go"))
                {
                    seen.Add(update);
                }
            });
            Assert.Equal(InvalidUsageMessage, error.Message);
            Assert.Empty(seen.OfType<UsageChatResponseUpdate>());
            Assert.Empty(seen.SelectMany(u => u.Contents).OfType<UsageContent>());
        }
    }

    [Fact]
    public async Task TwoIndependentToolRoundsAccumulateExactlyOnce()
    {
        var round = 0;
        string Responder(int _)
        {
            round++;
            return round == 1
                ? J(new
                {
                    id = "msg_1", type = "message", role = "assistant", model = Haiku,
                    content = new object[] { new { type = "tool_use", id = "toolu_1", name = "echo", input = new { } } },
                    stop_reason = "tool_use", usage = new { input_tokens = 10, cache_read_input_tokens = 5, output_tokens = 2 }
                })
                : AnthropicBody(Haiku, new { input_tokens = 20, cache_read_input_tokens = 30, output_tokens = 3 });
        }

        var (inner, http, wire) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), Responder);
        using (http)
        using (inner)
        {
            using var client = new ChatClientBuilder(inner).UseFunctionInvocation().Build();
            var response = await client.GetResponseAsync("go", new ChatOptions { Tools = [AIFunctionFactory.Create(() => "x", "echo")] });
            Assert.Equal(2, wire.Requests);
            Assert.Equal(15L + 50L, response.Usage!.InputTokenCount);                      // (10 + 5) + (20 + 30), each response counted once
            Assert.Equal(5L, response.Usage.OutputTokenCount);
            Assert.Equal(70L, response.Usage.TotalTokenCount);
        }

        // Streaming: two independent responses concatenated and aggregated by the standard extension add up, not double.
        var streamRound = 0;
        var (streamClient, http2, _) = Make(Claude(Haiku, VllmApiMode.AnthropicMessages), _ =>
        {
            streamRound++;
            return streamRound == 1
                ? AnthropicStream(Haiku, new { input_tokens = 10, cache_read_input_tokens = 5, output_tokens = 0 }, new { output_tokens = 2 })
                : AnthropicStream(Haiku, new { input_tokens = 20, cache_read_input_tokens = 30, output_tokens = 0 }, new { output_tokens = 3 });
        }, "text/event-stream");
        using (http2)
        using (streamClient)
        {
            var all = new List<ChatResponseUpdate>();
            all.AddRange(await Collect(streamClient));
            all.AddRange(await Collect(streamClient));
            var aggregated = all.ToChatResponse().Usage!;
            Assert.Equal(65L, aggregated.InputTokenCount);
            Assert.Equal(5L, aggregated.OutputTokenCount);
        }
    }

    // ---- Chat Completions usage -------------------------------------------------------------------------------------------------------------------------------

    private static string ChatBody(object? usage) => J(new
    {
        id = "chat_1", created = 1, model = "m",
        choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = "ok" } } },
        usage
    });

    [Theory]
    [InlineData("generic")]
    [InlineData("claude-chat")]
    [InlineData("deepseek-chat")]
    public async Task ChatCompletionsKeepsTheProviderTotalInputAndExposesTheCachedPartOnly(string flavor)
    {
        Func<HttpClient, IChatClient> factory = flavor switch
        {
            "claude-chat" => Claude(Haiku, VllmApiMode.ChatCompletions),     // its non-streaming override parses the response itself
            "deepseek-chat" => DeepSeek(VllmApiMode.ChatCompletions),
            _ => Generic(VllmApiMode.ChatCompletions),
        };
        var usage = new { prompt_tokens = 100, completion_tokens = 20, total_tokens = 120, prompt_tokens_details = new { cached_tokens = 60 } };
        var (client, http, _) = Make(factory, _ => ChatBody(usage));
        using (http)
        using (client)
        {
            var details = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(100L, details.InputTokenCount);                                     // already includes the cache; never 160
            Assert.Equal(20L, details.OutputTokenCount);
            Assert.Equal(120L, details.TotalTokenCount);
            Assert.Equal(60L, details.AdditionalCounts!["prompt_tokens_details.cached_tokens"]);
            Assert.Equal(60L, details.AdditionalCounts["cached_input_tokens"]);              // documented alias, not an extra consumption
        }
    }

    [Theory]
    [InlineData("""{"completion_tokens":20}""", null, 20L, null)]
    [InlineData("""{"prompt_tokens":100}""", 100L, null, null)]
    [InlineData("""{"prompt_tokens":100,"completion_tokens":20}""", 100L, 20L, 120L)]
    [InlineData("""{"total_tokens":120}""", null, null, 120L)]
    [InlineData("""{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0}""", 0L, 0L, 0L)]
    public async Task ChatCompletionsKeepsMissingCountersUnknownAndAnIndependentProviderTotal(string usageJson, long? input, long? output, long? total)
    {
        var body = ChatBody(JsonDocument.Parse(usageJson).RootElement.Clone());
        var (client, http, _) = Make(Claude(Haiku, VllmApiMode.ChatCompletions), _ => body);
        using (http)
        using (client)
        {
            var details = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(input, details.InputTokenCount);
            Assert.Equal(output, details.OutputTokenCount);
            Assert.Equal(total, details.TotalTokenCount);
            Assert.True(details.AdditionalCounts is null || details.AdditionalCounts.Count == 0);
        }
    }

    [Fact]
    public async Task ChatCompletionsWithoutUsageHasNoUsage()
    {
        var (client, http, _) = Make(Claude(Haiku, VllmApiMode.ChatCompletions), _ => ChatBody(null));
        using (http)
        using (client)
        {
            Assert.Null((await client.GetResponseAsync("go")).Usage);
        }
    }

    [Theory]
    [InlineData("""{"prompt_tokens":-1,"completion_tokens":1}""")]
    [InlineData("""{"prompt_tokens":1.5,"completion_tokens":1}""")]
    [InlineData("""{"prompt_tokens":"1","completion_tokens":1}""")]
    [InlineData("""{"prompt_tokens":99999999999999999999,"completion_tokens":1}""")]
    [InlineData("""{"prompt_tokens":9223372036854775807,"completion_tokens":1}""")]
    public async Task InvalidChatCountersRaiseTheFixedProtocolError(string usageJson)
    {
        var body = ChatBody(JsonDocument.Parse(usageJson).RootElement.Clone());
        var (client, http, _) = Make(Claude(Haiku, VllmApiMode.ChatCompletions), _ => body);
        using (http)
        using (client)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("go"));
            Assert.Equal(InvalidUsageMessage, error.Message);
        }
    }

    private static string ChatStream(IEnumerable<object> usageChunks, bool done)
    {
        var sse = new StringBuilder();
        sse.Append("data: " + J(new { id = "chat_s", choices = new[] { new { index = 0, delta = new { content = "ok" }, finish_reason = (string?)null } } }) + "\n\n");
        sse.Append("data: " + J(new { id = "chat_s", choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } } }) + "\n\n");
        foreach (var usage in usageChunks)
        {
            sse.Append("data: " + J(new { id = "chat_s", choices = Array.Empty<object>(), usage }) + "\n\n");
        }

        if (done)
        {
            sse.Append("data: [DONE]\n\n");
        }

        return sse.ToString();
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("deepseek")]
    public async Task ChatStreamingMergesCumulativeSnapshotsIntoOneUsageBeforeFinish(string flavor)
    {
        var first = new { prompt_tokens = 100, completion_tokens = 5, total_tokens = 105, prompt_tokens_details = new { cached_tokens = 60 } };
        var second = new { prompt_tokens = 100, completion_tokens = 20, total_tokens = 120, prompt_tokens_details = new { cached_tokens = 60 } };
        var factory = flavor == "generic" ? Generic(VllmApiMode.ChatCompletions) : DeepSeek(VllmApiMode.ChatCompletions);
        var (client, http, _) = Make(factory, _ => ChatStream([first, second], done: true), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var usageUpdate = Assert.Single(updates.OfType<UsageChatResponseUpdate>());
            Assert.Equal(100L, usageUpdate.Usage!.InputTokenCount);
            Assert.Equal(20L, usageUpdate.Usage.OutputTokenCount);
            Assert.Equal(120L, usageUpdate.Usage.TotalTokenCount);
            Assert.Equal(60L, usageUpdate.Usage.AdditionalCounts!["prompt_tokens_details.cached_tokens"]);
            Assert.Same(usageUpdate.Usage, Assert.Single(usageUpdate.Contents.OfType<UsageContent>()).Details);
            var aggregated = updates.ToChatResponse().Usage!;
            Assert.Equal(100L, aggregated.InputTokenCount);                                  // not doubled by the standard aggregation
            Assert.Equal(120L, aggregated.TotalTokenCount);
            Assert.True(updates.IndexOf(usageUpdate) < updates.FindIndex(u => u.FinishReason.HasValue));
            Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
        }
    }

    [Fact]
    public async Task ChatStreamingWithoutDoneStillEmitsTheKnownFinishButNoUsage()
    {
        var usage = new { prompt_tokens = 100, completion_tokens = 20, total_tokens = 120 };
        var (client, http, _) = Make(Generic(VllmApiMode.ChatCompletions), _ => ChatStream([usage], done: false), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
            Assert.Empty(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
            Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
        }
    }

    [Fact]
    public async Task ChatStreamingWithoutUsageEmitsNoUsageUpdate()
    {
        var (client, http, _) = Make(Generic(VllmApiMode.ChatCompletions), _ => ChatStream([], done: true), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
            Assert.Null(updates.ToChatResponse().Usage);
        }
    }

    [Fact]
    public async Task InvalidChatStreamingCountersAbortTheStreamWithTheFixedError()
    {
        var (client, http, _) = Make(Generic(VllmApiMode.ChatCompletions), _ => ChatStream([new { prompt_tokens = -5, completion_tokens = 1 }], done: true), "text/event-stream");
        using (http)
        using (client)
        {
            var seen = new List<ChatResponseUpdate>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var update in client.GetStreamingResponseAsync("go"))
                {
                    seen.Add(update);
                }
            });
            Assert.Equal(InvalidUsageMessage, error.Message);
            Assert.Empty(seen.OfType<UsageChatResponseUpdate>());
        }
    }

    // ---- Responses usage --------------------------------------------------------------------------------------------------------------------------------------

    private static string ResponsesBody(object? usage) => J(new
    {
        id = "resp_1", @object = "response", created_at = 1, status = "completed", model = "m",
        output = new object[] { new { id = "msg_1", type = "message", role = "assistant", content = new object[] { new { type = "output_text", text = "ok" } } } },
        usage
    });

    [Fact]
    public async Task ResponsesNonStreamingKeepsTheProviderTotalInputAndExposesTheCachedPart()
    {
        var usage = new { input_tokens = 100, output_tokens = 20, total_tokens = 120, input_tokens_details = new { cached_tokens = 60 } };
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => ResponsesBody(usage));
        using (http)
        using (client)
        {
            var details = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(100L, details.InputTokenCount);
            Assert.Equal(20L, details.OutputTokenCount);
            Assert.Equal(120L, details.TotalTokenCount);
            Assert.Equal(60L, details.AdditionalCounts!["input_tokens_details.cached_tokens"]);
            Assert.Equal(60L, details.AdditionalCounts["cached_input_tokens"]);
        }
    }

    [Theory]
    [InlineData("""{"output_tokens":20}""", null, 20L, null)]
    [InlineData("""{"input_tokens":100}""", 100L, null, null)]
    [InlineData("""{"input_tokens":100,"output_tokens":20}""", 100L, 20L, 120L)]
    [InlineData("""{"input_tokens":0,"output_tokens":0,"total_tokens":0}""", 0L, 0L, 0L)]
    public async Task ResponsesKeepsMissingCountersUnknown(string usageJson, long? input, long? output, long? total)
    {
        var body = ResponsesBody(JsonDocument.Parse(usageJson).RootElement.Clone());
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => body);
        using (http)
        using (client)
        {
            var details = (await client.GetResponseAsync("go")).Usage!;
            Assert.Equal(input, details.InputTokenCount);
            Assert.Equal(output, details.OutputTokenCount);
            Assert.Equal(total, details.TotalTokenCount);
        }
    }

    private static string ResponsesStream(object? usage, bool completed)
    {
        var sse = new StringBuilder();
        sse.Append("data: " + J(new { type = "response.output_text.delta", delta = "ok" }) + "\n\n");
        sse.Append("data: " + J(new { type = "response.output_item.done", output_index = 0, item = new { id = "item-a", type = "function_call", call_id = "call-a", name = "first", arguments = "{}" } }) + "\n\n");
        if (completed)
        {
            sse.Append("data: " + J(new { type = "response.completed", response = new { id = "resp_s", status = "completed", usage } }) + "\n\n");
            sse.Append("data: [DONE]\n\n");
        }

        return sse.ToString();
    }

    [Fact]
    public async Task ResponsesStreamingEmitsOneUsageBeforeFinishAndDoesNotDoubleOnAggregation()
    {
        var usage = new { input_tokens = 100, output_tokens = 20, total_tokens = 120, input_tokens_details = new { cached_tokens = 60 } };
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => ResponsesStream(usage, completed: true), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            var usageUpdate = Assert.Single(updates.OfType<UsageChatResponseUpdate>());
            Assert.Equal(100L, usageUpdate.Usage!.InputTokenCount);
            Assert.Equal(60L, usageUpdate.Usage.AdditionalCounts!["input_tokens_details.cached_tokens"]);
            Assert.Same(usageUpdate.Usage, Assert.Single(usageUpdate.Contents.OfType<UsageContent>()).Details);
            Assert.Equal(120L, updates.ToChatResponse().Usage!.TotalTokenCount);
            Assert.True(updates.IndexOf(usageUpdate) < updates.FindIndex(u => u.FinishReason.HasValue));
            Assert.Equal(ChatFinishReason.ToolCalls, updates[^1].FinishReason);
        }
    }

    [Fact]
    public async Task ResponsesStreamingWithoutACompletionEventKeepsTheKnownFinishAndEmitsNoUsage()
    {
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => ResponsesStream(null, completed: false), "text/event-stream");
        using (http)
        using (client)
        {
            var updates = await Collect(client);
            Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
            Assert.Equal(ChatFinishReason.ToolCalls, updates[^1].FinishReason);
        }
    }

    [Fact]
    public async Task ResponsesStreamingWithoutUsageEmitsNoUsageUpdate()
    {
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => ResponsesStream(null, completed: true), "text/event-stream");
        using (http)
        using (client)
        {
            Assert.Empty((await Collect(client)).OfType<UsageChatResponseUpdate>());
        }
    }

    [Fact]
    public async Task InvalidResponsesCountersRaiseTheFixedProtocolErrorInBothModes()
    {
        var body = ResponsesBody(new { input_tokens = 1.5, output_tokens = 1 });
        var (client, http, _) = Make(Generic(VllmApiMode.Responses), _ => body);
        using (http)
        using (client)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("go"));
            Assert.Equal(InvalidUsageMessage, error.Message);
        }

        var (streamClient, http2, _) = Make(Generic(VllmApiMode.Responses), _ => ResponsesStream(new { input_tokens = -1, output_tokens = 1 }, completed: true), "text/event-stream");
        using (http2)
        using (streamClient)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { await foreach (var _ in streamClient.GetStreamingResponseAsync("go")) { } });
            Assert.Equal(InvalidUsageMessage, error.Message);
        }
    }

    // ---- cancellation and read failures: no usage is invented ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A response body that hands out its scripted bytes and then runs <paramref name="tail"/> instead of ending: the tail either waits until the caller cancels or
    /// throws, so the stream never reaches a termination marker.
    /// </summary>
    private sealed class ScriptedStream(byte[] first, Func<CancellationToken, Task> tail) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < first.Length)
            {
                var count = Math.Min(buffer.Length, first.Length - _position);
                first.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await tail(cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }

    private static (IChatClient Client, HttpClient Http) MakeScripted(Func<HttpClient, IChatClient> factory, string firstPart, Func<CancellationToken, Task> tail)
    {
        var http = new HttpClient(new Fake(_ =>
        {
            var content = new StreamContent(new ScriptedStream(Encoding.UTF8.GetBytes(firstPart), tail));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        return (factory(http), http);
    }

    private static Task WaitUntilCancelled(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);

    private static Task ConnectionReset(CancellationToken _) => throw new IOException("simulated connection reset");

    private static (Func<HttpClient, IChatClient> Factory, string FirstPart) StreamingScenario(string protocol) => protocol switch
    {
        // Each script carries real usage and a stop reason but no termination marker (no message_stop / [DONE] / response.completed).
        "anthropic-haiku" => (Claude(Haiku, VllmApiMode.AnthropicMessages), AnthropicStream(Haiku, CachedUsage, new { output_tokens = 7 }, stop: false)),
        "anthropic-sonnet" => (Claude(Sonnet, VllmApiMode.AnthropicMessages), AnthropicStream(Sonnet, CachedUsage, new { output_tokens = 7 }, stop: false)),
        "chat" => (Generic(VllmApiMode.ChatCompletions), ChatStream([new { prompt_tokens = 100, completion_tokens = 20, total_tokens = 120 }], done: false)),
        "responses" => (Generic(VllmApiMode.Responses), ResponsesStream(null, completed: false)),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    private static void AssertNoUsageWasInvented(IEnumerable<ChatResponseUpdate> seen)
    {
        var updates = seen.ToList();
        Assert.Empty(updates.OfType<UsageChatResponseUpdate>());
        Assert.Empty(updates.SelectMany(u => u.Contents).OfType<UsageContent>());
        Assert.DoesNotContain(updates, u => u.FinishReason.HasValue);          // an aborted stream is not presented as a finished one
    }

    [Theory]
    [InlineData("anthropic-haiku")]
    [InlineData("anthropic-sonnet")]
    [InlineData("chat")]
    [InlineData("responses")]
    public async Task CancellingAStreamMidwayEmitsNoUsageAndNoFinish(string protocol)
    {
        var (factory, firstPart) = StreamingScenario(protocol);
        var (client, http) = MakeScripted(factory, firstPart, WaitUntilCancelled);
        using (http)
        using (client)
        using (var cancellation = new CancellationTokenSource())
        {
            var seen = new List<ChatResponseUpdate>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var update in client.GetStreamingResponseAsync("go", null, cancellation.Token))
                {
                    seen.Add(update);
                    if (update.Contents.OfType<TextContent>().Any())
                    {
                        cancellation.Cancel();                                       // the caller gives up after the first text
                    }
                }
            });

            Assert.Contains(seen, u => u.Contents.OfType<TextContent>().Any());   // content that was already delivered stays delivered
            AssertNoUsageWasInvented(seen);
        }
    }

    [Theory]
    [InlineData("anthropic-haiku")]
    [InlineData("anthropic-sonnet")]
    [InlineData("chat")]
    [InlineData("responses")]
    public async Task AReadFailureMidStreamSurfacesAndEmitsNoUsageAndNoFinish(string protocol)
    {
        var (factory, firstPart) = StreamingScenario(protocol);
        var (client, http) = MakeScripted(factory, firstPart, ConnectionReset);
        using (http)
        using (client)
        {
            var seen = new List<ChatResponseUpdate>();
            var error = await Assert.ThrowsAsync<IOException>(async () =>
            {
                await foreach (var update in client.GetStreamingResponseAsync("go"))
                {
                    seen.Add(update);
                }
            });

            Assert.Equal("simulated connection reset", error.Message);
            Assert.Contains(seen, u => u.Contents.OfType<TextContent>().Any());
            AssertNoUsageWasInvented(seen);
        }
    }

    [Theory]
    [InlineData("anthropic-haiku")]
    [InlineData("anthropic-sonnet")]
    [InlineData("chat")]
    [InlineData("responses")]
    public async Task ANonStreamingBodyThatFailsToReadSurfacesTheFailureAndInventsNoResponse(string protocol)
    {
        var (factory, _) = StreamingScenario(protocol);
        var (client, http) = MakeScripted(factory, string.Empty, ConnectionReset);
        using (http)
        using (client)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync("go"));
            Assert.True(ChainContains<IOException>(error), "The read failure must stay visible in the exception chain.");
            Assert.False(ChainContains<InvalidOperationException>(error));        // not mistaken for a usage-protocol error
        }
    }

    private static bool ChainContains<T>(Exception? exception) where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }
}
