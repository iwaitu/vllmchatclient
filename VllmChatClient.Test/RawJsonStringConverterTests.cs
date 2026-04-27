using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VllmChatClient.Test;

public class RawJsonStringConverterTests
{
    [Fact]
    public void JsonObjectString_IsSerializedAsString()
    {
        var payload = new ConverterProbe
        {
            Content = "{\"foo\":\"bar\"}"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal("{\"foo\":\"bar\"}", content.GetString());
    }

    [Fact]
    public void JsonArrayString_IsSerializedAsRawArray()
    {
        var payload = new ConverterProbe
        {
            Content = "[{\"type\":\"text\",\"text\":\"hello\"}]"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("hello", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task AssistantMessage_WithReasoningContent_RoundTripsToRequest()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new TestChatClient("https://example.test/v1/{0}", httpClient);

        var assistantMessage = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_123", "Search", new Dictionary<string, object?> { ["question"] = "南宁火车站在哪里？" })])
        {
            RawRepresentation = new VllmChatResponseMessage
            {
                Role = "assistant",
                ReasoningContent = JsonDocument.Parse("[{\"type\":\"text\",\"text\":\"tool reasoning\"}]").RootElement.Clone(),
                ToolCalls =
                [
                    new VllmToolCall
                    {
                        Id = "call_123",
                        Type = "function",
                        Function = new VllmFunctionToolCall
                        {
                            Name = "Search",
                            Arguments = "{\"question\":\"南宁火车站在哪里？\"}"
                        }
                    }
                ]
            }
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "帮我查一下地址"),
            assistantMessage,
            new(ChatRole.Tool, [new FunctionResultContent("call_123", "南宁市青秀区方圆广场北面站前路1号。")])
        };

        _ = await client.GetResponseAsync(messages);

        Assert.NotNull(handler.RequestJson);
        using var doc = JsonDocument.Parse(handler.RequestJson!);
        var serializedAssistant = doc.RootElement.GetProperty("messages")[1];
        Assert.True(serializedAssistant.TryGetProperty("reasoning_content", out var reasoningContent));
        Assert.Equal(JsonValueKind.Array, reasoningContent.ValueKind);
        Assert.Equal("tool reasoning", reasoningContent[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task AssistantMessage_WithStreamedToolCallReasoningContent_RoundTripsToRequest()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new TestChatClient("https://example.test/v1/{0}", httpClient);

        var toolCall = new FunctionCallContent("call_456", "Search", new Dictionary<string, object?> { ["question"] = "南宁火车站地址" })
        {
            RawRepresentation = new Delta
            {
                ReasoningContent = "stream reasoning"
            }
        };
        toolCall.AdditionalProperties ??= [];
        toolCall.AdditionalProperties["reasoning_content"] = "stream reasoning";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "帮我查一下地址"),
            new(ChatRole.Assistant, [toolCall]),
            new(ChatRole.Tool, [new FunctionResultContent("call_456", "南宁市青秀区方圆广场北面站前路1号。")])
        };

        _ = await client.GetResponseAsync(messages);

        Assert.NotNull(handler.RequestJson);
        using var doc = JsonDocument.Parse(handler.RequestJson!);
        var serializedAssistant = doc.RootElement.GetProperty("messages")[1];
        Assert.Equal("stream reasoning", serializedAssistant.GetProperty("reasoning_content").GetString());
    }

    private sealed class ConverterProbe
    {
        [JsonPropertyName("content")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class TestChatClient(string endpoint, HttpClient httpClient) : VllmBaseChatClient(endpoint, token: null, modelId: "test-model", httpClient)
    {
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new VllmChatResponse
                {
                    Id = "resp_123",
                    Created = 1,
                    Model = "test-model",
                    Choices =
                    [
                        new Choice
                        {
                            FinishReason = "stop",
                            Message = new VllmChatResponseMessage
                            {
                                Role = "assistant",
                                Content = "ok"
                            }
                        }
                    ]
                }, JsonContext.Default.VllmChatResponse)
            };
        }
    }
}
