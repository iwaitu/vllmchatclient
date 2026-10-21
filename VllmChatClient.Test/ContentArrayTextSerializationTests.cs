using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using Microsoft.Extensions.AI.VllmChatClient.Mimo;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// Message text that happens to be a JSON array must reach the provider as a JSON string.
/// Only content parts the client builds from typed media contents may become a content array.
/// All payloads are synthetic and captured offline by a fake HttpMessageHandler.
/// </summary>
public class ContentArrayTextSerializationTests
{
    private static readonly Dictionary<string, string> ArrayTexts = new()
    {
        // A model answer echoed back for a format repair: no "type" on the first element.
        ["keyed_objects"] = """[{"key":"attr:body_identity","verdict":"confirmed","value":"x","quote":"q"}]""",
        // A schema-shaped answer: "type" exists but is not a content-part type.
        ["schema_shaped"] = """[{"type":"object","additionalProperties":false,"required":["a"],"properties":{"a":{"type":"string"}}}]""",
        // Looks exactly like a text content part, but it is still text the caller wrote.
        ["content_part_lookalike"] = """[{"type":"text","text":"still plain text"}]""",
        ["scalars"] = "[1,2,3]",
        ["empty"] = "[]",
        ["leading_whitespace"] = " \n\t[{\"status\":\"ok\"}]",
        ["object_control"] = """{"key":"attr:body_identity","verdict":"confirmed"}""",
    };

    private static readonly string[] Providers =
        ["base", "qwen3-next", "qwen3", "gpt-oss", "gemini3", "minimax", "glm", "kimi", "deepseek-v3", "deepseek-r1", "openai-gpt", "mimo", "nemotron", "gemma4", "claude", "qwq"];

    public static IEnumerable<object[]> TextCases()
    {
        foreach (var provider in Providers)
        foreach (var stream in new[] { false, true })
        foreach (var kind in ArrayTexts.Keys)
            yield return [provider, stream, kind];
    }

    [Theory]
    [MemberData(nameof(TextCases))]
    public async Task ArrayShapedMessageText_RemainsStringInActualHttpBody(string provider, bool stream, string kind)
    {
        var text = ArrayTexts[kind];
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient(provider, http);
        ChatMessage[] history =
        [
            new(ChatRole.System, "Answer with one JSON object."),
            new(ChatRole.User, text),
            new(ChatRole.Assistant, text),
            new(ChatRole.User, "Return a JSON object, not an array."),
        ];

        await SendAsync(client, history, stream);

        var messages = ReadMessages(handler.Body);
        Assert.Equal(4, messages.Length);
        foreach (var message in messages)
            Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
        Assert.Equal(text, messages[1].GetProperty("content").GetString());
        Assert.Equal(text, messages[2].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamedAssistantChunks_AreJoinedIntoOneStringNotParts(bool stream)
    {
        // Streaming history arrives as several TextContent items that only form an array when joined.
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient("qwen3-next", http);
        ChatMessage[] history =
        [
            new(ChatRole.User, "Compare the values."),
            new(ChatRole.Assistant, [new TextContent("[{\"status\":\"ok\","), new TextContent("\"compared_count\":0}"), new TextContent("]")]),
            new(ChatRole.User, "Return a JSON object."),
        ];

        await SendAsync(client, history, stream);

        var assistant = ReadMessages(handler.Body)[1].GetProperty("content");
        Assert.Equal(JsonValueKind.String, assistant.ValueKind);
        Assert.Equal("[{\"status\":\"ok\",\"compared_count\":0}]", assistant.GetString());
    }

    [Theory]
    [InlineData("base", false)]
    [InlineData("base", true)]
    [InlineData("qwen3-next", false)]
    [InlineData("qwen3-next", true)]
    [InlineData("gpt-oss", false)]
    [InlineData("gemini3", true)]
    [InlineData("kimi", false)]
    public async Task MixedToolHistory_KeepsTextToolCallsAndArrayToolResults(string provider, bool stream)
    {
        const string answer = """[{"key":"attr:a","verdict":"confirmed"}]""";
        const string toolResult = """[{"type":"text","text":"tool data, not content parts"},{"id":"doc-1"}]""";
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient(provider, http);
        ChatMessage[] history =
        [
            new(ChatRole.User, "Read the document."),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "list_documents", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)]),
            new(ChatRole.Assistant, answer),
            new(ChatRole.User, "Return a JSON object."),
        ];

        await SendAsync(client, history, stream);

        var messages = ReadMessages(handler.Body);
        var call = messages.Single(m => m.TryGetProperty("tool_calls", out _));
        Assert.Equal("call-1", call.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        var tool = messages.Single(m => m.GetProperty("role").GetString() == "tool");
        Assert.Equal("call-1", tool.GetProperty("tool_call_id").GetString());
        Assert.Equal(JsonValueKind.String, tool.GetProperty("content").ValueKind);
        Assert.Equal(toolResult, tool.GetProperty("content").GetString());
        var assistant = messages.Single(m => m.GetProperty("role").GetString() == "assistant"
            && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == answer);
        Assert.False(assistant.TryGetProperty("tool_calls", out _));
        Assert.All(messages.Where(m => m.TryGetProperty("content", out _)),
            m => Assert.Equal(JsonValueKind.String, m.GetProperty("content").ValueKind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qwen3NextMedia_IsPartsArrayWhileArrayTextHistoryStaysString(bool stream)
    {
        const string answer = """[{"key":"attr:a"}]""";
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient("qwen3-next", http);
        ChatMessage[] history =
        [
            new(ChatRole.User,
            [
                new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
                new UriContent("https://example.test/frame.jpg", "image/jpeg"),
                new UriContent("https://example.test/clip.mp4", "video/mp4"),
                new DataContent(new byte[] { 0, 1 }, "video/mp4"),
                new TextContent(answer),
            ]),
            new(ChatRole.Assistant, answer),
            new(ChatRole.User, [new TextContent(answer)]),
        ];

        await SendAsync(client, history, stream);

        var messages = ReadMessages(handler.Body);
        var media = messages[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, media.ValueKind);
        Assert.Equal(["image_url", "image_url", "video_url", "video_url", "text"],
            media.EnumerateArray().Select(p => p.GetProperty("type").GetString()));
        Assert.StartsWith("data:image/png;base64,", media[0].GetProperty("image_url").GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal("https://example.test/frame.jpg", media[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("https://example.test/clip.mp4", media[2].GetProperty("video_url").GetProperty("url").GetString());
        Assert.StartsWith("data:video/mp4;base64,", media[3].GetProperty("video_url").GetProperty("url").GetString(), StringComparison.Ordinal);
        // Array-shaped text beside media is a text part, never spliced into the parts list.
        Assert.Equal(answer, media[4].GetProperty("text").GetString());
        Assert.False(messages[0].TryGetProperty("images", out _));
        Assert.Equal(answer, messages[1].GetProperty("content").GetString());
        Assert.Equal(answer, messages[2].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GemmaParts_AreArraysAndArrayTextIsATextPart(bool stream)
    {
        // Gemma 3 always sends typed text/image parts; the array text lives inside a text part.
        const string answer = """[{"key":"attr:a"}]""";
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient("gemma3", http);
        ChatMessage[] history =
        [
            new(ChatRole.User, [new TextContent("Describe."), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]),
            new(ChatRole.Assistant, answer),
        ];

        await SendAsync(client, history, stream);

        var messages = ReadMessages(handler.Body);
        var user = messages[0].GetProperty("content");
        Assert.Equal(["text", "image_url"], user.EnumerateArray().Select(p => p.GetProperty("type").GetString()));
        Assert.Equal("data:image/png;base64,AQID", user[1].GetProperty("image_url").GetProperty("url").GetString());
        var assistant = messages[1].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, assistant.ValueKind);
        Assert.Single(assistant.EnumerateArray());
        Assert.Equal("text", assistant[0].GetProperty("type").GetString());
        Assert.Equal(answer, assistant[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("base", false)]
    [InlineData("base", true)]
    [InlineData("gpt-oss", false)]
    [InlineData("gemini3", false)]
    public async Task ImageInputs_UseImagesFieldAndArrayTextStaysString(string provider, bool stream)
    {
        const string answer = """[{"key":"attr:a"}]""";
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient(provider, http);
        ChatMessage[] history =
        [
            new(ChatRole.User, [new TextContent(answer), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]),
        ];

        await SendAsync(client, history, stream);

        var user = ReadMessages(handler.Body).Single(m => m.GetProperty("role").GetString() == "user");
        Assert.Equal(answer, user.GetProperty("content").GetString());
        Assert.Equal("AQID", user.GetProperty("images")[0].GetString());
    }

    [Theory]
    [InlineData("base", false)]
    [InlineData("qwen3-next", true)]
    public async Task AudioInput_DoesNotInventContentParts(string provider, bool stream)
    {
        // No client maps audio to an OpenAI content part; audio stays unsent and the text stays a string.
        const string answer = """[{"key":"attr:a"}]""";
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using var client = CreateClient(provider, http);
        ChatMessage[] history =
        [
            new(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "audio/wav"), new TextContent(answer)]),
        ];

        await SendAsync(client, history, stream);

        var user = ReadMessages(handler.Body)[0];
        Assert.Equal(JsonValueKind.String, user.GetProperty("content").ValueKind);
        Assert.Equal(answer, user.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(VllmApiMode.Responses, false)]
    [InlineData(VllmApiMode.Responses, true)]
    [InlineData(VllmApiMode.AnthropicMessages, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true)]
    public async Task OtherApiModes_SendArrayShapedTextAsText(VllmApiMode mode, bool stream)
    {
        const string answer = """[{"key":"attr:a","verdict":"confirmed"}]""";
        using var handler = new CaptureHandler(stream, mode);
        using var http = new HttpClient(handler);
        using var client = new VllmQwen3NextChatClient("https://example.test/v1", modelId: "qwen3.8-27b", httpClient: http, apiMode: mode);
        ChatMessage[] history =
        [
            new(ChatRole.User, answer),
            new(ChatRole.Assistant, answer),
            new(ChatRole.User, "Return a JSON object."),
        ];

        await SendAsync(client, history, stream);

        using var doc = JsonDocument.Parse(handler.Body!);
        if (mode == VllmApiMode.Responses)
        {
            var input = doc.RootElement.GetProperty("input");
            Assert.Equal(answer, input[0].GetProperty("content").GetString());
            Assert.Equal(answer, input[1].GetProperty("content").GetString());
        }
        else
        {
            var messages = doc.RootElement.GetProperty("messages");
            foreach (var index in new[] { 0, 1 })
            {
                var blocks = messages[index].GetProperty("content");
                var textBlock = blocks.ValueKind == JsonValueKind.Array ? blocks[0] : blocks;
                Assert.Equal(answer, textBlock.ValueKind == JsonValueKind.String ? textBlock.GetString() : textBlock.GetProperty("text").GetString());
            }
        }
    }

    [Fact]
    public void ExplicitParts_AreTheOnlyWayToSerializeAContentArray()
    {
        const string parts = """[{"type":"text","text":"hello"}]""";
        var text = new VllmOpenAIChatRequestMessage { Role = "user", Content = parts };
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(text, JsonContext.Default.VllmOpenAIChatRequestMessage)))
        {
            Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("content").ValueKind);
            Assert.Equal(parts, json.RootElement.GetProperty("content").GetString());
        }

        using var partsDoc = JsonDocument.Parse(parts);
        var typed = new VllmOpenAIChatRequestMessage
        {
            Role = "user",
            Content = "hello",
            ContentParts = partsDoc.RootElement.EnumerateArray().Select(p => p.Clone()).ToArray(),
        };
        using var typedJson = JsonDocument.Parse(JsonSerializer.Serialize(typed, JsonContext.Default.VllmOpenAIChatRequestMessage));
        Assert.True(JsonElement.DeepEquals(partsDoc.RootElement, typedJson.RootElement.GetProperty("content")));
    }

    [Fact]
    public void ContentRoundTrip_KeepsTextAndPartsDistinct()
    {
        var text = JsonSerializer.Deserialize("""{"role":"assistant","content":"[1,2]"}""", JsonContext.Default.VllmOpenAIChatRequestMessage)!;
        Assert.Equal("[1,2]", text.Content);
        Assert.Null(text.ContentParts);

        var parts = JsonSerializer.Deserialize("""{"role":"user","content":[{"type":"text","text":"hi"}]}""", JsonContext.Default.VllmOpenAIChatRequestMessage)!;
        Assert.NotNull(parts.ContentParts);
        Assert.Equal("text", parts.ContentParts![0].GetProperty("type").GetString());
    }

    private static IChatClient CreateClient(string provider, HttpClient http) => provider switch
    {
        "qwen3-next" => new VllmQwen3NextChatClient("https://example.test/v1", modelId: "qwen3.8-27b", httpClient: http),
        "qwen3" => new VllmQwen3ChatClient("https://example.test/v1", modelId: "qwen3", httpClient: http),
        "gpt-oss" => new VllmGptOssChatClient("https://example.test/v1", modelId: "gpt-oss-120b", httpClient: http),
        "gemini3" => new VllmGemini3ChatClient("https://example.test/v1", modelId: "gemini-3-pro", httpClient: http),
        "gemma3" => new VllmGemmaChatClient("https://example.test/v1", modelId: "gemma3", httpClient: http),
        "gemma4" => new VllmGemma4ChatClient("https://example.test/v1", modelId: "gemma-4-31b-it", httpClient: http),
        "minimax" => new VllmMiniMaxChatClient("https://example.test/v1", modelId: "MiniMax-M2.1", httpClient: http),
        "glm" => new VllmGlmChatClient("https://example.test/v1", modelId: "glm-4", httpClient: http),
        "kimi" => new VllmKimiK2ChatClient("https://example.test/v1", modelId: "kimi-k2-thinking", httpClient: http),
        "deepseek-v3" => new VllmDeepseekV3ChatClient("https://example.test/v1", modelId: "deepseek-v4-flash", httpClient: http),
        "deepseek-r1" => new VllmDeepseekR1ChatClient("https://example.test/v1", modelId: "deepseekr1", httpClient: http),
        "openai-gpt" => new VllmOpenAiGptClient("https://example.test/v1", modelId: "openai/gpt-5.2-codex", httpClient: http),
        "mimo" => new VllmMimoChatClient("https://example.test/v1", modelId: "mimo-v2-pro", httpClient: http),
        "nemotron" => new VllmNemotronChatClient("https://example.test/v1", modelId: "nvidia/nemotron-3-super-120b-a12b:free", httpClient: http),
        "claude" => new VllmClaudeChatClient("https://example.test/v1", modelId: "anthropic/claude-opus-4.6", httpClient: http),
        "qwq" => new VllmQwqChatClient("https://example.test/v1", modelId: "qwq", httpClient: http),
        _ => new BaseClient(http),
    };

    private static async Task SendAsync(IChatClient client, ChatMessage[] history, bool stream)
    {
        if (stream)
            await foreach (var _ in client.GetStreamingResponseAsync(history)) { }
        else
            await client.GetResponseAsync(history);
    }

    private static JsonElement[] ReadMessages(string? body)
    {
        Assert.NotNull(body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.Clone()).ToArray();
    }

    private sealed class BaseClient(HttpClient http) : VllmBaseChatClient("https://example.test/v1", token: null, modelId: "test-model", httpClient: http);

    private sealed class CaptureHandler(bool stream, VllmApiMode mode = VllmApiMode.ChatCompletions) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var payload = stream ? "data: [DONE]\n\n" : mode switch
            {
                VllmApiMode.Responses => """{"id":"resp-1","status":"completed","output":[]}""",
                VllmApiMode.AnthropicMessages => """{"id":"msg-1","type":"message","role":"assistant","content":[],"stop_reason":"end_turn"}""",
                _ => """{"id":"chat-1","created":1,"model":"m","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
