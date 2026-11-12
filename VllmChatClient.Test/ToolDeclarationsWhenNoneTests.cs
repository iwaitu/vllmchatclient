using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Offline wire-shape tests for <see cref="VllmToolDeclarations.KeepWhenToolModeNoneKey"/>: tool definitions stay on the
/// request under <see cref="ChatToolMode.None"/> only when the caller opts in.
/// </summary>
public class ToolDeclarationsWhenNoneTests
{
    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task WithoutTheKey_NoneModeSendsNoTools(VllmApiMode mode)
    {
        var (root, body) = await CaptureAsync(mode, ChatToolMode.None, value: null, setKey: false);

        Assert.False(root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array);
        AssertToolChoiceNone(root, mode);
        Assert.DoesNotContain(VllmToolDeclarations.KeepWhenToolModeNoneKey, body);
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task WithTheKey_NoneModeKeepsTheSameToolsAsAutoMode(VllmApiMode mode)
    {
        var (auto, _) = await CaptureAsync(mode, ChatToolMode.Auto, value: null, setKey: false);
        var (none, body) = await CaptureAsync(mode, ChatToolMode.None, value: true, setKey: true);

        // Byte-identical definitions are what keeps the provider's cached prefix valid.
        Assert.Equal(auto.GetProperty("tools").GetRawText(), none.GetProperty("tools").GetRawText());
        AssertToolChoiceNone(none, mode);
        Assert.DoesNotContain(VllmToolDeclarations.KeepWhenToolModeNoneKey, body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task FalseOrNull_KeepsTheDefault(bool? value)
    {
        var (root, _) = await CaptureAsync(VllmApiMode.ChatCompletions, ChatToolMode.None, value, setKey: true);

        Assert.False(root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task TheKeyDoesNotChangeAutoMode()
    {
        var (plain, _) = await CaptureAsync(VllmApiMode.ChatCompletions, ChatToolMode.Auto, value: null, setKey: false);
        var (keyed, _) = await CaptureAsync(VllmApiMode.ChatCompletions, ChatToolMode.Auto, value: true, setKey: true);

        Assert.Equal(plain.GetRawText(), keyed.GetRawText());
    }

    [Fact]
    public async Task NonBooleanValue_IsRejectedBeforeAnyRequest()
    {
        var requests = 0;
        using var http = new HttpClient(new CaptureHandler(_ =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        using var client = new TestClient(http, VllmApiMode.ChatCompletions);
        var options = new ChatOptions
        {
            Tools = [Tool()],
            ToolMode = ChatToolMode.None,
            AdditionalProperties = new AdditionalPropertiesDictionary { [VllmToolDeclarations.KeepWhenToolModeNoneKey] = "yes" },
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather")], options));
        Assert.Equal(0, requests);
    }

    private static void AssertToolChoiceNone(JsonElement root, VllmApiMode mode)
    {
        var choice = root.GetProperty("tool_choice");
        Assert.Equal(
            "none",
            mode == VllmApiMode.AnthropicMessages ? choice.GetProperty("type").GetString() : choice.GetString());
    }

    private static AIFunction Tool() => AIFunctionFactory.Create(
        (string city) => city,
        new AIFunctionFactoryOptions { Name = "get_weather", Description = "Get weather." });

    private static async Task<(JsonElement Root, string Body)> CaptureAsync(
        VllmApiMode mode,
        ChatToolMode toolMode,
        bool? value,
        bool setKey)
    {
        string? body = null;
        using var http = new HttpClient(new CaptureHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Reply(mode), Encoding.UTF8, "application/json")
            };
        }));
        using var client = new TestClient(http, mode);
        var options = new ChatOptions { Tools = [Tool()], ToolMode = toolMode };
        if (setKey)
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [VllmToolDeclarations.KeepWhenToolModeNoneKey] = value,
            };
        }

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather")], options);

        Assert.NotNull(body);
        return (JsonDocument.Parse(body!).RootElement.Clone(), body!);
    }

    private static string Reply(VllmApiMode mode) => mode switch
    {
        VllmApiMode.AnthropicMessages =>
            """{"id":"m","type":"message","role":"assistant","model":"none-test","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""",
        VllmApiMode.Responses =>
            """{"id":"r","object":"response","status":"completed","model":"none-test","output":[{"id":"m","type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}""",
        _ =>
            """{"id":"c","model":"none-test","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""",
    };

    private sealed class TestClient : VllmBaseChatClient
    {
        public TestClient(HttpClient httpClient, VllmApiMode mode)
            : base("http://localhost:8000/v1", null, modelId: "none-test", httpClient, mode)
        {
        }
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
