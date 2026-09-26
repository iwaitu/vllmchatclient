using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class Qwen3NextToolHistoryTests
{
    [Theory]
    [InlineData(VllmApiMode.Responses, false, false)]
    [InlineData(VllmApiMode.Responses, true, false)]
    [InlineData(VllmApiMode.ChatCompletions, false, false)]
    [InlineData(VllmApiMode.ChatCompletions, true, false)]
    [InlineData(VllmApiMode.AnthropicMessages, false, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true, false)]
    [InlineData(VllmApiMode.Responses, false, true)]
    [InlineData(VllmApiMode.Responses, true, true)]
    public async Task ToolHistory_PreservesNativeCallsAndResultsInHttpBody(VllmApiMode mode, bool stream, bool useBaseClient)
    {
        using var handler = new CaptureHandler(mode, stream);
        using var http = new HttpClient(handler);
        using VllmBaseChatClient client = useBaseClient
            ? new BaseClient(http, mode)
            : new VllmQwen3NextChatClient("https://example.test/v1", modelId: "qwen3.8-27b", httpClient: http, apiMode: mode);
        var arguments = new Dictionary<string, object?> { ["city"] = "上海", ["days"] = 2 };
        ChatMessage[] history =
        [
            new(ChatRole.User, "Check weather"),
            new(ChatRole.Assistant, [new TextContent("Checking "), new TextContent("weather"),
                new FunctionCallContent("call-a", "weather", arguments),
                new FunctionCallContent("call-b", "weather", arguments)]),
            new(ChatRole.Tool, [
                new FunctionResultContent("call-b", new Dictionary<string, object?> { ["temperature"] = 21 }),
                new FunctionResultContent("call-a", "sunny")]),
            new(ChatRole.Assistant, [new FunctionCallContent("call-c", "weather", arguments)]),
            new(ChatRole.Tool, [new FunctionResultContent("call-c", "rain")]),
        ];
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string city, int days) => "sunny", "weather")],
            ToolMode = ChatToolMode.RequireSpecific("weather"),
        };

        if (stream)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(history, options)) { }
        }
        else
        {
            await client.GetResponseAsync(history, options);
        }

        Assert.NotNull(handler.Body);
        using var doc = JsonDocument.Parse(handler.Body);
        var root = doc.RootElement;
        Assert.Equal(stream, root.GetProperty("stream").GetBoolean());
        Assert.DoesNotContain("tool_call>", root.ToString());
        Assert.DoesNotContain("tool_response>", root.ToString());

        if (mode == VllmApiMode.Responses)
        {
            Assert.Equal("/v1/responses", handler.Uri?.AbsolutePath);
            var input = root.GetProperty("input").EnumerateArray().ToArray();
            Assert.Equal(8, input.Length);
            Assert.Equal("Checking weather", input[1].GetProperty("content").GetString());
            var calls = input.Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "function_call").ToArray();
            Assert.Equal(new[] { "call-a", "call-b", "call-c" }, calls.Select(x => x.GetProperty("call_id").GetString()));
            foreach (var call in calls)
            {
                Assert.Equal("weather", call.GetProperty("name").GetString());
                using var args = JsonDocument.Parse(call.GetProperty("arguments").GetString()!);
                Assert.Equal("上海", args.RootElement.GetProperty("city").GetString());
                Assert.Equal(2, args.RootElement.GetProperty("days").GetInt32());
            }
            var results = input.Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "function_call_output").ToArray();
            Assert.Equal(new[] { "call-b", "call-a", "call-c" }, results.Select(x => x.GetProperty("call_id").GetString()));
            using var result = JsonDocument.Parse(results[0].GetProperty("output").GetString()!);
            Assert.Equal(21, result.RootElement.GetProperty("temperature").GetInt32());
            Assert.Equal("sunny", results[1].GetProperty("output").GetString());
            Assert.Equal("rain", results[2].GetProperty("output").GetString());
            Assert.Equal("weather", root.GetProperty("tools")[0].GetProperty("name").GetString());
            Assert.False(root.GetProperty("tools")[0].TryGetProperty("function", out _));
            Assert.Equal("weather", root.GetProperty("tool_choice").GetProperty("name").GetString());
            Assert.False(root.GetProperty("tool_choice").TryGetProperty("function", out _));
        }
        else if (mode == VllmApiMode.ChatCompletions)
        {
            Assert.Equal("/v1/chat/completions", handler.Uri?.AbsolutePath);
            var messages = root.GetProperty("messages");
            Assert.Equal("Checking weather", messages[1].GetProperty("content").GetString());
            Assert.Equal(new[] { "call-a", "call-b" }, messages[1].GetProperty("tool_calls").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
            Assert.Equal("tool", messages[2].GetProperty("role").GetString());
            Assert.Equal("call-b", messages[2].GetProperty("tool_call_id").GetString());
            Assert.Equal("call-a", messages[3].GetProperty("tool_call_id").GetString());
            Assert.Equal("call-c", messages[4].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        }
        else
        {
            Assert.Equal("/v1/messages", handler.Uri?.AbsolutePath);
            var blocks = root.GetProperty("messages").EnumerateArray()
                .Where(x => x.GetProperty("content").ValueKind == JsonValueKind.Array)
                .SelectMany(x => x.GetProperty("content").EnumerateArray()).ToArray();
            Assert.Equal(new[] { "call-a", "call-b", "call-c" }, blocks.Where(x => x.GetProperty("type").GetString() == "tool_use").Select(x => x.GetProperty("id").GetString()));
            Assert.Equal(new[] { "call-b", "call-a", "call-c" }, blocks.Where(x => x.GetProperty("type").GetString() == "tool_result").Select(x => x.GetProperty("tool_use_id").GetString()));
        }

        Assert.Equal(4, history[1].Contents.Count);
        Assert.Equal("call-a", ((FunctionCallContent)history[1].Contents[2]).CallId);
    }

    private sealed class BaseClient(HttpClient http, VllmApiMode mode)
        : VllmBaseChatClient("https://example.test/v1", token: null, modelId: "qwen3.8-27b", httpClient: http, apiMode: mode);

    private sealed class CaptureHandler(VllmApiMode mode, bool stream) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri;
            var payload = stream ? "data: [DONE]\n\n" : mode switch
            {
                VllmApiMode.Responses => """{"id":"resp-1","status":"completed","output":[]} """,
                VllmApiMode.AnthropicMessages => """{"id":"msg-1","type":"message","role":"assistant","content":[],"stop_reason":"end_turn"} """,
                _ => """{"id":"chat-1","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]} """,
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
