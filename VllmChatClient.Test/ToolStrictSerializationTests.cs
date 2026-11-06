using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Offline wire-shape tests for tool-level <c>strict</c>. The flag is opt-in through
/// <c>AIFunction.AdditionalProperties["strictJsonSchema"]</c> and must be omitted when unset.
/// </summary>
public class ToolStrictSerializationTests
{
    private const string Key = "strictJsonSchema";

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions, true)]
    [InlineData(VllmApiMode.ChatCompletions, false)]
    [InlineData(VllmApiMode.Responses, true)]
    [InlineData(VllmApiMode.Responses, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true)]
    [InlineData(VllmApiMode.AnthropicMessages, false)]
    public async Task ExplicitStrict_IsSerializedOnTheToolDefinition(VllmApiMode mode, bool strict)
    {
        var root = await CaptureToolAsync(mode, strict);

        var strictElement = StrictHolder(root, mode);
        Assert.Equal(strict ? JsonValueKind.True : JsonValueKind.False, strictElement.ValueKind);
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task UnsetStrict_IsOmitted(VllmApiMode mode)
    {
        var root = await CaptureToolAsync(mode, strict: null);

        var holder = mode == VllmApiMode.ChatCompletions
            ? root.GetProperty("tools")[0].GetProperty("function")
            : root.GetProperty("tools")[0];
        Assert.False(holder.TryGetProperty("strict", out _));
    }

    [Fact]
    public async Task JsonElementBooleanStrict_IsHonored()
    {
        var root = await CaptureToolAsync(VllmApiMode.ChatCompletions, strict: null, rawValue: JsonDocument.Parse("true").RootElement);

        Assert.Equal(JsonValueKind.True, root.GetProperty("tools")[0].GetProperty("function").GetProperty("strict").ValueKind);
    }

    [Fact]
    public async Task NonBooleanStrict_IsIgnored()
    {
        var root = await CaptureToolAsync(VllmApiMode.ChatCompletions, strict: null, rawValue: "yes");

        Assert.False(root.GetProperty("tools")[0].GetProperty("function").TryGetProperty("strict", out _));
    }

    private static JsonElement StrictHolder(JsonElement root, VllmApiMode mode)
    {
        var tool = root.GetProperty("tools")[0];
        // Chat Completions nests under function; Responses and Anthropic put it on the tool itself.
        return mode == VllmApiMode.ChatCompletions
            ? tool.GetProperty("function").GetProperty("strict")
            : tool.GetProperty("strict");
    }

    private static async Task<JsonElement> CaptureToolAsync(VllmApiMode mode, bool? strict, object? rawValue = null)
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

        var additional = new Dictionary<string, object?>();
        if (strict is { } flag)
        {
            additional[Key] = flag;
        }
        else if (rawValue is not null)
        {
            additional[Key] = rawValue;
        }

        var tool = AIFunctionFactory.Create(
            (string city) => city,
            new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get weather.",
                AdditionalProperties = additional,
            });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "weather")],
            new ChatOptions { Tools = [tool] });

        Assert.NotNull(body);
        return JsonDocument.Parse(body!).RootElement.Clone();
    }

    private static string Reply(VllmApiMode mode) => mode switch
    {
        VllmApiMode.AnthropicMessages =>
            """{"id":"m","type":"message","role":"assistant","model":"strict-test","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""",
        VllmApiMode.Responses =>
            """{"id":"r","object":"response","status":"completed","model":"strict-test","output":[{"id":"m","type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}""",
        _ =>
            """{"id":"c","model":"strict-test","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""",
    };

    private sealed class TestClient : VllmBaseChatClient
    {
        public TestClient(HttpClient httpClient, VllmApiMode mode)
            : base("http://localhost:8000/v1", null, modelId: "strict-test", httpClient, mode)
        {
        }
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
