using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class AnthropicApiModeTests
{
    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldPostMessagesRequest()
    {
        string? requestJson = null;
        Uri? requestUri = null;
        HttpRequestHeadersSnapshot? headers = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestUri = request.RequestUri;
            headers = new HttpRequestHeadersSnapshot(
                request.Headers.Contains("x-api-key"),
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.SingleOrDefault() : null);
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-1",
                  "type": "message",
                  "role": "assistant",
                  "model": "claude-test",
                  "content": [{ "type": "text", "text": "hello" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 2, "output_tokens": 3 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", "test-key", httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "You are concise."),
                new ChatMessage(ChatRole.User, "hi")
            ],
            new ChatOptions { MaxOutputTokens = 123 });

        Assert.Equal("/v1/messages", requestUri?.AbsolutePath);
        Assert.True(headers?.HasApiKey);
        Assert.Equal("2023-06-01", headers?.AnthropicVersion);
        Assert.Equal("hello", response.Text);
        Assert.Equal(5, response.Usage?.TotalTokenCount);

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        Assert.Equal("claude-test", root.GetProperty("model").GetString());
        Assert.Equal(123, root.GetProperty("max_tokens").GetInt32());
        var system = root.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        Assert.Equal("You are concise.", system[0].GetProperty("text").GetString());
        Assert.Equal("hi", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldParseToolUse()
    {
        var handler = new CaptureHttpMessageHandler(_ => Task.FromResult(JsonResponse("""
            {
              "id": "msg-2",
              "type": "message",
              "role": "assistant",
              "model": "claude-test",
              "content": [
                {
                  "type": "tool_use",
                  "id": "toolu-1",
                  "name": "GetWeather",
                  "input": { "city": "Singapore" }
                }
              ],
              "stop_reason": "tool_use",
              "usage": { "input_tokens": 4, "output_tokens": 5 }
            }
            """)));

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", null, httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather")]);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages[0].Contents));

        Assert.Equal("GetWeather", call.Name);
        Assert.Equal("toolu-1", call.CallId);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldSerializeRequiredToolChoice()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-3",
                  "type": "message",
                  "role": "assistant",
                  "model": "claude-test",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", null, httpClient, VllmApiMode.AnthropicMessages);
        var tool = AIFunctionFactory.Create(
            (string city) => $"weather:{city}",
            new AIFunctionFactoryOptions { Name = "GetWeather", Description = "Get weather." });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "weather")],
            new ChatOptions
            {
                Tools = [tool],
                ToolMode = ChatToolMode.RequireSpecific("GetWeather")
            });

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        var toolChoice = root.GetProperty("tool_choice");
        Assert.Equal("tool", toolChoice.GetProperty("type").GetString());
        Assert.Equal("GetWeather", toolChoice.GetProperty("name").GetString());
        Assert.Equal("GetWeather", root.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_AssistantToolReplay_ShouldIncludeThinkingBlock()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-thinking-replay",
                  "type": "message",
                  "role": "assistant",
                  "model": "claude-test",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", null, httpClient, VllmApiMode.AnthropicMessages);

        var functionCall = new FunctionCallContent("toolu-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Nanning" });
        functionCall.AdditionalProperties ??= [];
        functionCall.AdditionalProperties["anthropic_thinking"] = "First think, then call the tool.";

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.Assistant, [functionCall]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu-1", "rain")])
            ]);

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("thinking", content[0].GetProperty("type").GetString());
        Assert.Equal("First think, then call the tool.", content[0].GetProperty("thinking").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("GetWeather", content[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_MultipleToolResults_ShouldShareImmediateUserMessage()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-multi-tool-results",
                  "type": "message",
                  "role": "assistant",
                  "model": "claude-test",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/v1", null, httpClient, VllmApiMode.AnthropicMessages);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "where is the station and will it rain?"),
                new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("toolu-search", "Search", new Dictionary<string, object?> { ["question"] = "station address" }),
                    new FunctionCallContent("toolu-weather", "GetWeather", new Dictionary<string, object?>())
                ]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu-search", "station address")]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu-weather", "rain")])
            ]);

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var messages = doc.RootElement.GetProperty("messages");

        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("tool_use", messages[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", messages[1].GetProperty("content")[1].GetProperty("type").GetString());

        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        var toolResults = messages[2].GetProperty("content");
        Assert.Equal(2, toolResults.GetArrayLength());
        Assert.Equal("tool_result", toolResults[0].GetProperty("type").GetString());
        Assert.Equal("toolu-search", toolResults[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_result", toolResults[1].GetProperty("type").GetString());
        Assert.Equal("toolu-weather", toolResults[1].GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task DeepseekAnthropic_FunctionInvokingFollowUp_ShouldSerializeArrayContentForAllMessages()
    {
        var requestBodies = new List<string>();
        int callCount = 0;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            callCount++;

            if (callCount == 1)
            {
                return JsonResponse("""
                    {
                      "id": "msg-tool-1",
                      "type": "message",
                      "role": "assistant",
                      "model": "deepseek-v4-flash",
                      "content": [
                        { "type": "thinking", "thinking": "Need weather." },
                        { "type": "tool_use", "id": "toolu-1", "name": "GetWeather", "input": { "city": "Nanning" } }
                      ],
                      "stop_reason": "tool_use",
                      "usage": { "input_tokens": 3, "output_tokens": 1 }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "id": "msg-final-1",
                  "type": "message",
                  "role": "assistant",
                  "model": "deepseek-v4-flash",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com/anthropic", "test-key", "deepseek-v4-flash", httpClient, VllmApiMode.AnthropicMessages);

        var tool = AIFunctionFactory.Create(
            (string city) => $"weather:{city}",
            new AIFunctionFactoryOptions { Name = "GetWeather", Description = "Get weather." });

        IChatClient invokingClient = new ChatClientBuilder(client)
            .UseFunctionInvocation()
            .Build();

        _ = await invokingClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "weather")],
            new VllmChatOptions { ThinkingEnabled = true, Tools = [tool] });

        Assert.True(requestBodies.Count >= 2);
        using var doc = JsonDocument.Parse(requestBodies[1]);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Array, message.GetProperty("content").ValueKind);
        }
    }

    [Fact]
    public async Task DeepseekAnthropic_StreamingFunctionInvokingFollowUp_ShouldReplayThinkingBlock()
    {
        var requestBodies = new List<string>();
        int callCount = 0;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            callCount++;

            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        event: message_start
                        data: {"type":"message_start","message":{"id":"msg-stream-tool","type":"message","role":"assistant","model":"deepseek-v4-flash","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}

                        event: content_block_start
                        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

                        event: content_block_delta
                        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Need weather."}}

                        event: content_block_stop
                        data: {"type":"content_block_stop","index":0}

                        event: content_block_start
                        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu-weather","name":"GetWeather","input":{}}}

                        event: content_block_delta
                        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}

                        event: content_block_stop
                        data: {"type":"content_block_stop","index":1}

                        event: message_delta
                        data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"input_tokens":1,"output_tokens":2}}

                        event: message_stop
                        data: {"type":"message_stop"}

                        """,
                        Encoding.UTF8,
                        "text/event-stream")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    event: message_start
                    data: {"type":"message_start","message":{"id":"msg-stream-final","type":"message","role":"assistant","model":"deepseek-v4-flash","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}

                    event: content_block_start
                    data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                    event: content_block_delta
                    data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"rain"}}

                    event: message_delta
                    data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":1,"output_tokens":1}}

                    event: message_stop
                    data: {"type":"message_stop"}

                    """,
                    Encoding.UTF8,
                    "text/event-stream")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmDeepseekV3ChatClient("https://api.deepseek.com/anthropic", "test-key", "deepseek-v4-flash", httpClient, VllmApiMode.AnthropicMessages);
        var tool = AIFunctionFactory.Create(
            () => "rain",
            new AIFunctionFactoryOptions { Name = "GetWeather", Description = "Get weather." });

        IChatClient invokingClient = new ChatClientBuilder(client)
            .UseFunctionInvocation()
            .Build();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in invokingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "weather")],
            new VllmChatOptions { ThinkingEnabled = true, Tools = [tool] }))
        {
            updates.Add(update);
        }

        Assert.True(requestBodies.Count >= 2);
        using var doc = JsonDocument.Parse(requestBodies[1]);
        var messages = doc.RootElement.GetProperty("messages");
        var assistantContent = messages[1].GetProperty("content");

        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("thinking", assistantContent[0].GetProperty("type").GetString());
        Assert.Equal("Need weather.", assistantContent[0].GetProperty("thinking").GetString());
        Assert.Equal("tool_use", assistantContent[1].GetProperty("type").GetString());
        Assert.Equal("toolu-weather", assistantContent[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Qwen3Next_AnthropicMode_ShouldSerializeRequiredToolChoice()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-qwen-tool-choice",
                  "type": "message",
                  "role": "assistant",
                  "model": "qwen3.6-plus",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmQwen3NextChatClient("http://localhost:8000/v1", null, "qwen3.6-plus", httpClient, VllmApiMode.AnthropicMessages);
        var tool = AIFunctionFactory.Create(
            (string path) => $"read:{path}",
            new AIFunctionFactoryOptions { Name = "read_file", Description = "Read file." });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "read")],
            new ChatOptions
            {
                Tools = [tool],
                ToolMode = ChatToolMode.RequireSpecific("read_file")
            });

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        var toolChoice = root.GetProperty("tool_choice");
        Assert.Equal("tool", toolChoice.GetProperty("type").GetString());
        Assert.Equal("read_file", toolChoice.GetProperty("name").GetString());
        Assert.Equal("read_file", root.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GlmClient_AnthropicMode_ShouldSerializeDisabledThinking()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-4",
                  "type": "message",
                  "role": "assistant",
                  "model": "glm-test",
                  "content": [{ "type": "text", "text": "ok" }],
                  "stop_reason": "end_turn",
                  "usage": { "input_tokens": 3, "output_tokens": 1 }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new VllmGlmChatClient("http://localhost:8000/v1", null, "glm-test", httpClient, VllmApiMode.AnthropicMessages);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new VllmChatOptions { ThinkingEnabled = false });

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        Assert.Equal("disabled", doc.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task BaseClient_AnthropicModeStreaming_ShouldReadAnthropicEvents()
    {
        var handler = new CaptureHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                event: message_start
                data: {"type":"message_start","message":{"id":"msg-stream","type":"message","role":"assistant","model":"claude-test","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}

                event: content_block_start
                data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hel"}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"lo"}}

                event: message_delta
                data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":1,"output_tokens":2}}

                event: message_stop
                data: {"type":"message_stop"}

                """,
                Encoding.UTF8,
                "text/event-stream")
        }));

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", null, httpClient, VllmApiMode.AnthropicMessages);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(update);
        }

        Assert.Equal("hello", string.Concat(updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(c => c.Text)));
        Assert.Contains(updates, u => u is UsageChatResponseUpdate usage && usage.Usage?.TotalTokenCount == 3);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record HttpRequestHeadersSnapshot(bool HasApiKey, string? AnthropicVersion);

    private sealed class TestVllmChatClient : VllmBaseChatClient
    {
        public TestVllmChatClient(string endpoint, string? token, HttpClient httpClient, VllmApiMode apiMode)
            : base(endpoint, token, modelId: "claude-test", httpClient, apiMode)
        {
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
