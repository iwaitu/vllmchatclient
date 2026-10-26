using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Glm4;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// 离线录制 HTTP 请求测试：GLM-5.3-Flash 始终思考，不能发送 thinking disabled / enable_thinking=false。
/// 这里只验证 SDK 发出的 wire 请求，不代表真实服务已通过。
/// </summary>
public class Glm53FlashThinkingCompatibilityTests
{
    private const string Flash = "glm-5.3-flash";

    private static readonly ChatMessage[] Hello = [new(ChatRole.User, "hello")];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flash_ThinkingDisabled_MapsToEnabledWithLowEffort(bool stream)
    {
        var root = await SendAsync(Flash, Hello, new GlmChatOptions { ThinkingEnabled = false }, stream);

        AssertAlwaysThinking(root, "low");
    }

    [Theory]
    [InlineData("ZHIPU/GLM-5.3-Flash")]
    [InlineData("zhipu/glm-5.3-flash")]
    [InlineData("GLM-5.3-Flash")]
    [InlineData("  glm-5.3-flash ")]
    public async Task Flash_ModelIdForms_AreRecognised(string modelId)
    {
        var root = await SendAsync(modelId, Hello, new GlmChatOptions { ThinkingEnabled = false });

        AssertAlwaysThinking(root, "low");
    }

    [Theory]
    [InlineData("glm-5.3-flashx")]
    [InlineData("glm-5.3-flash-x")]
    [InlineData("glm-5.3-flash-2")]
    [InlineData("glm-5.3")]
    [InlineData("glm-5.1")]
    [InlineData("zhipu/zhipu/glm-5.3-flash")]
    [InlineData("xzhipu/glm-5.3-flash")]
    public async Task OtherModels_KeepLegacyDisabledBehaviour(string modelId)
    {
        var root = await SendAsync(modelId, Hello, new GlmChatOptions { ThinkingEnabled = false, ReasoningEffort = "high" });

        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.False(root.TryGetProperty("enable_thinking", out _));
    }

    [Fact]
    public async Task LegacyModel_ThinkingEnabled_IgnoresReasoningEffort()
    {
        var root = await SendAsync("glm-5.1", Hello, new GlmChatOptions { ThinkingEnabled = true, ReasoningEffort = "max" });

        Assert.Equal("enabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task Flash_ThinkingDisabled_WithConflictingHighEffort_UsesLow()
    {
        var root = await SendAsync(Flash, Hello, new GlmChatOptions { ThinkingEnabled = false, ReasoningEffort = "high" });

        AssertAlwaysThinking(root, "low");
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("high", "high")]
    [InlineData("max", "max")]
    [InlineData("MAX", "max")]
    [InlineData(" High ", "high")]
    public async Task Flash_ThinkingEnabled_RespectsLegalExplicitEffort(string effort, string expected)
    {
        var root = await SendAsync(Flash, Hello, new GlmChatOptions { ThinkingEnabled = true, ReasoningEffort = effort });

        AssertAlwaysThinking(root, expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Flash_ThinkingEnabled_WithBlankEffort_KeepsModelDefault(string? effort)
    {
        var root = await SendAsync(Flash, Hello, new GlmChatOptions { ThinkingEnabled = true, ReasoningEffort = effort });

        Assert.Equal("enabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.False(root.TryGetProperty("enable_thinking", out _));
    }

    [Theory]
    [InlineData("medium", false)]
    [InlineData("xhigh", false)]
    [InlineData("medium", true)]
    public async Task Flash_ThinkingEnabled_WithUnsupportedEffort_ThrowsBeforeAnyRequest(string effort, bool stream)
    {
        var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = new VllmGlmChatClient("https://example.test/v1", "key", Flash, http);
        var options = new GlmChatOptions { ThinkingEnabled = true, ReasoningEffort = effort };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            if (stream)
            {
                await foreach (var _ in client.GetStreamingResponseAsync(Hello, options)) { }
            }
            else
            {
                await client.GetResponseAsync(Hello, options);
            }
        });
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("xhigh")]
    public async Task Flash_ThinkingDisabled_WithUnsupportedEffort_StillUsesLow(string effort)
    {
        var root = await SendAsync(Flash, Hello, new GlmChatOptions { ThinkingEnabled = false, ReasoningEffort = effort });

        AssertAlwaysThinking(root, "low");
    }

    [Fact]
    public async Task OptionsModelId_OverridesClientDefault_InBothDirections()
    {
        var toFlash = await SendAsync("glm-5.1", Hello, new GlmChatOptions { ModelId = Flash, ThinkingEnabled = false });
        AssertAlwaysThinking(toFlash, "low");

        var awayFromFlash = await SendAsync(Flash, Hello, new GlmChatOptions { ModelId = "glm-5.1", ThinkingEnabled = false });
        Assert.Equal("glm-5.1", awayFromFlash.GetProperty("model").GetString());
        Assert.Equal("disabled", awayFromFlash.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(awayFromFlash.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task PlainChatOptions_DoNotEnableThinkingPolicy()
    {
        var root = await SendAsync(Flash, Hello, new ChatOptions { Temperature = 0.5f });

        Assert.False(root.TryGetProperty("thinking", out _));
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.False(root.TryGetProperty("enable_thinking", out _));
    }

    [Fact]
    public async Task Flash_ToolCallAndHistoryReplay_AreNotBroken()
    {
        var tool = AIFunctionFactory.Create(GetWeather, "get_weather");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "weather in Nanning?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Nanning" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "rainy")]),
            new(ChatRole.User, "thanks"),
        };
        var options = new GlmChatOptions { ThinkingEnabled = false, Tools = [tool] };

        var root = await SendAsync(Flash, history, options);

        AssertAlwaysThinking(root, "low");
        Assert.Equal("get_weather", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        var messages = root.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(["user", "assistant", "tool", "user"], messages.Select(m => m.GetProperty("role").GetString()).ToArray());
        Assert.Equal("call_1", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task RepeatedCalls_DoNotPolluteOptionsOrEachOther()
    {
        var extra = new AdditionalPropertiesDictionary
        {
            ["enable_thinking"] = false,
            ["reasoning_effort"] = "high",
            ["thinking"] = new { type = "disabled" },
        };
        var options = new GlmChatOptions { ThinkingEnabled = false, ReasoningEffort = "high", AdditionalProperties = extra };
        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };

        var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var client = new VllmGlmChatClient("https://example.test/v1", "key", Flash, http);

        await client.GetResponseAsync(messages, options);
        var first = Parse(handler.Body);
        await client.GetResponseAsync(messages, options);
        var second = Parse(handler.Body);

        options.ThinkingEnabled = true;
        options.ReasoningEffort = "max";
        await client.GetResponseAsync(messages, options);
        var third = Parse(handler.Body);

        options.ThinkingEnabled = false;
        await client.GetResponseAsync(messages, options);
        var fourth = Parse(handler.Body);

        AssertAlwaysThinking(first, "low");
        AssertAlwaysThinking(second, "low");
        AssertAlwaysThinking(third, "max");
        AssertAlwaysThinking(fourth, "low");

        // 调用方对象保持原样，且 AdditionalProperties 不会穿透到 wire。
        Assert.False(options.ThinkingEnabled);
        Assert.Equal("max", options.ReasoningEffort);
        Assert.Same(extra, options.AdditionalProperties);
        Assert.Equal(3, extra.Count);
        Assert.Equal(false, extra["enable_thinking"]);
        Assert.Equal("high", extra["reasoning_effort"]);
        Assert.Single(messages);
        Assert.Equal("hello", messages[0].Text);
    }

    private static string GetWeather([Description("city")] string city) => "rainy";

    private static void AssertAlwaysThinking(JsonElement root, string expectedEffort)
    {
        Assert.Equal("enabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(expectedEffort, root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.TryGetProperty("enable_thinking", out _));
        Assert.False(root.TryGetProperty("chat_template_kwargs", out _));
        Assert.False(root.TryGetProperty("extra_body", out _));
        Assert.DoesNotContain("disabled", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enable_thinking", root.GetRawText(), StringComparison.Ordinal);
    }

    private static JsonElement Parse(string? body)
    {
        Assert.NotNull(body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> SendAsync(
        string clientModel, IEnumerable<ChatMessage> messages, ChatOptions options, bool stream = false)
    {
        var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = new VllmGlmChatClient("https://example.test/v1", "key", clientModel, http);

        if (stream)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
        }
        else
        {
            await client.GetResponseAsync(messages, options);
        }

        return Parse(handler.Body);
    }

    private sealed class CaptureHandler(bool stream = false) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var payload = stream
                ? "data: [DONE]\n\n"
                : """{"id":"chat-1","created":1,"model":"m","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
