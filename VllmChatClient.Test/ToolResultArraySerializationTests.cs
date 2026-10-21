using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class ToolResultArraySerializationTests
{
    public static IEnumerable<object[]> ArrayResults()
    {
        foreach (var provider in new[] { "base", "qwen" })
        foreach (var stream in new[] { false, true })
        foreach (var kind in new[] { "string", "element", "objects", "numbers", "empty", "content_parts" })
            yield return [provider, stream, kind];
        foreach (var provider in new[] { "gpt-oss", "gemini" })
        foreach (var stream in new[] { false, true })
            yield return [provider, stream, "string"];
    }

    [Theory]
    [MemberData(nameof(ArrayResults))]
    public async Task ToolArrayResult_RemainsTextInActualHttpBody(string provider, bool stream, string kind)
    {
        const string documents = "[{\"id\":\"doc-1\",\"title\":\"规划指标\",\"value\":2.5}]";
        var expected = kind switch
        {
            "numbers" => "[1,2,3]",
            "empty" => "[]",
            "content_parts" => "[{\"type\":\"text\",\"text\":\"This is tool data, not message content parts.\"}]",
            _ => documents
        };
        object value = kind switch
        {
            "element" => JsonDocument.Parse(documents).RootElement.Clone(),
            "objects" => new object[] { new Dictionary<string, object?> { ["id"] = "doc-1", ["title"] = "规划指标", ["value"] = 2.5 } },
            _ => expected
        };
        using var handler = new CaptureHandler(stream);
        using var http = new HttpClient(handler);
        using IChatClient client = provider switch
        {
            "qwen" => new VllmQwen3NextChatClient("https://example.test/v1", modelId: "qwen3.8-27b", httpClient: http),
            "gpt-oss" => new VllmGptOssChatClient("https://example.test/v1", modelId: "gpt-oss-120b", httpClient: http),
            "gemini" => new VllmGemini3ChatClient("https://example.test/v1", modelId: "gemini-3-pro", httpClient: http),
            _ => new BaseClient(http)
        };
        ChatMessage[] messages =
        [
            new(ChatRole.User, "Read the document."),
            new(ChatRole.Assistant, [new FunctionCallContent("call-documents", "list_documents", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("call-documents", value)])
        ];
        if (stream)
            await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }
        else
            await client.GetResponseAsync(messages);

        using var request = JsonDocument.Parse(handler.Body!);
        var tool = request.RootElement.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("role").GetString() == "tool");
        Assert.Equal("call-documents", tool.GetProperty("tool_call_id").GetString());
        var content = tool.GetProperty("content");
        Assert.Equal(JsonValueKind.String, content.ValueKind);
        using var actual = JsonDocument.Parse(content.GetString()!);
        using var desired = JsonDocument.Parse(expected);
        Assert.True(JsonElement.DeepEquals(desired.RootElement, actual.RootElement));
        if (value is string text)
            Assert.Equal(text, content.GetString());
    }

    [Fact]
    public void MultimodalParts_KeepTheirArrayShapeWithSourceGeneratedSerialization()
    {
        const string parts = """[{"type":"text","text":"Describe the media."},{"type":"image_url","image_url":{"url":"data:image/png;base64,AQID"}},{"type":"video_url","video_url":{"url":"data:video/mp4;base64,AQID"}}]""";
        using var expected = JsonDocument.Parse(parts);
        // Content parts are explicit since 2.0.27; array-shaped Content text stays a string.
        var message = new VllmOpenAIChatRequestMessage
        {
            Role = "user",
            Content = "Describe the media.",
            ContentParts = expected.RootElement.EnumerateArray().Select(part => part.Clone()).ToArray(),
        };
        var json = JsonSerializer.Serialize(message, JsonContext.Default.VllmOpenAIChatRequestMessage);
        using var request = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, request.RootElement.GetProperty("content").ValueKind);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, request.RootElement.GetProperty("content")));
    }

    [Fact]
    public void NullAssistantContent_RemainsOmitted()
    {
        var message = new VllmOpenAIChatRequestMessage { Role = "assistant", Content = null };
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(message, JsonContext.Default.VllmOpenAIChatRequestMessage));
        Assert.False(request.RootElement.TryGetProperty("content", out _));
    }

    private sealed class BaseClient(HttpClient http) : VllmBaseChatClient("https://example.test/v1", token: null, modelId: "test-model", httpClient: http);

    private sealed class CaptureHandler(bool stream) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(stream ? "data: [DONE]\n\n" : """{"id":"chat-review","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                    Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
