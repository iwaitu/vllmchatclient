using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class AnthropicApiModeTests
{
    [Fact]
    public async Task BaseClient_AnthropicMode_NoneToolMode_ShouldExplicitlyDisableTools()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {"id":"msg-none","type":"message","role":"assistant","content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("https://example.test/v1", "fake-key", httpClient, VllmApiMode.AnthropicMessages);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Answer without tools")], new VllmChatOptions
        {
            ThinkingEnabled = true,
            Tools = [AIFunctionFactory.Create(() => "sunny", "GetWeather")],
            ToolMode = ChatToolMode.None,
        });

        using var doc = JsonDocument.Parse(requestJson!);
        Assert.Equal("none", doc.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.False(doc.RootElement.TryGetProperty("tools", out _));
        Assert.Equal("enabled", doc.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

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

    [Theory]
    [InlineData(true, "enabled")]
    [InlineData(false, "disabled")]
    public async Task BaseClient_AnthropicMode_ShouldSerializeThinkingFromVllmOptions(bool thinkingEnabled, string expectedType)
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "id": "msg-thinking-options",
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
            [new ChatMessage(ChatRole.User, "hi")],
            new VllmChatOptions { ThinkingEnabled = thinkingEnabled, MaxOutputTokens = 4096 });

        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson!);
        var thinking = doc.RootElement.GetProperty("thinking");
        Assert.Equal(expectedType, thinking.GetProperty("type").GetString());

        if (thinkingEnabled)
        {
            Assert.Equal(1024, thinking.GetProperty("budget_tokens").GetInt32());
        }
        else
        {
            Assert.False(thinking.TryGetProperty("budget_tokens", out _));
        }
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

    // ---- Anthropic structured outputs: output_config.format (JSON schema) ----------------------------------------------------------------------------------

    private const string EmailSchemaJson = """{"type":"object","properties":{"name":{"type":"string"},"email":{"type":"string"},"plan_interest":{"type":"string"},"demo_requested":{"type":"boolean"}},"required":["name","email","plan_interest","demo_requested"],"additionalProperties":false}""";

    private const string EmailAnswer = """{"name":"John Smith","email":"john@example.com","plan_interest":"Enterprise","demo_requested":true}""";

    private static readonly string EmailMessageResponse =
        """{"id":"msg-so","type":"message","role":"assistant","model":"claude-test","content":[{"type":"text","text":"{\"name\":\"John Smith\",\"email\":\"john@example.com\",\"plan_interest\":\"Enterprise\",\"demo_requested\":true}"}],"stop_reason":"end_turn","usage":{"input_tokens":5,"output_tokens":9}}""";

    private static ChatResponseFormat SchemaFormat(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), "email_info", "Key information from an email");
    }

    private static readonly ChatMessage[] EmailPrompt =
    [
        new ChatMessage(ChatRole.User, "Extract the key information from this email: John Smith (john@example.com) is interested in our Enterprise plan and wants to schedule a demo for next Tuesday at 2pm.")
    ];

    /// <summary>The entity actually posted, the request headers and how many requests reached the transport.</summary>
    private sealed class WireRecorder
    {
        public string? Body { get; private set; }
        public int Requests { get; private set; }
        public bool HadBetaHeader { get; private set; }

        public HttpMessageHandler Handler(string responseJson) => new CaptureHttpMessageHandler(async request =>
        {
            await Record(request);
            return JsonResponse(responseJson);
        });

        public HttpMessageHandler StreamHandler() => new CaptureHttpMessageHandler(async request =>
        {
            await Record(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "event: message_start\n" + """data: {"type":"message_start","message":{"id":"msg-so","type":"message","role":"assistant","model":"claude-test","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}""" + "\n\n"
                    + "event: content_block_start\n" + """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""" + "\n\n"
                    + "event: content_block_delta\n" + """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"name\":\"John Smith\","}}""" + "\n\n"
                    + "event: content_block_delta\n" + """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"\"email\":\"john@example.com\",\"plan_interest\":\"Enterprise\",\"demo_requested\":true}"}}""" + "\n\n"
                    + "event: message_delta\n" + """data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":1,"output_tokens":9}}""" + "\n\n"
                    + "event: message_stop\n" + """data: {"type":"message_stop"}""" + "\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            };
        });

        private async Task Record(HttpRequestMessage request)
        {
            Requests++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            HadBetaHeader = request.Headers.Contains("anthropic-beta");
        }
    }

    private static void AssertOutputConfigCarriesSchema(string body, string expectedSchemaJson)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var outputConfig = root.GetProperty("output_config");
        Assert.Equal(["format"], outputConfig.EnumerateObject().Select(p => p.Name));
        var format = outputConfig.GetProperty("format");
        Assert.Equal(["type", "schema"], format.EnumerateObject().Select(p => p.Name));     // no OpenAI name / strict / description wrapper
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(format.GetProperty("schema").GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(expectedSchemaJson)), "the schema must reach the wire semantically unchanged");
        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("format", out _));
        Assert.DoesNotContain("email_info", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"strict\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_ShouldSendJsonSchemaAsOutputConfigFormat()
    {
        var wire = new WireRecorder();
        using var httpClient = new HttpClient(wire.Handler(EmailMessageResponse));
        using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync(EmailPrompt, new ChatOptions { MaxOutputTokens = 1024, ResponseFormat = SchemaFormat(EmailSchemaJson) });

        Assert.Equal(1, wire.Requests);
        Assert.False(wire.HadBetaHeader);                                                     // output_config needs no beta header
        AssertOutputConfigCarriesSchema(wire.Body!, EmailSchemaJson);
        Assert.Equal(EmailAnswer, response.Text);
        using var answer = JsonDocument.Parse(response.Text);
        Assert.True(answer.RootElement.GetProperty("demo_requested").GetBoolean());
    }

    [Fact]
    public async Task ClaudeClient_AnthropicMode_ShouldSendJsonSchemaAsOutputConfigFormat()
    {
        var wire = new WireRecorder();
        using var httpClient = new HttpClient(wire.Handler(EmailMessageResponse));
        using var client = new VllmClaudeChatClient("http://localhost:8000/v1", "test-key", "claude-test", httpClient, VllmApiMode.AnthropicMessages);

        var response = await client.GetResponseAsync(EmailPrompt, new VllmChatOptions { ThinkingEnabled = false, MaxOutputTokens = 256, ResponseFormat = SchemaFormat(EmailSchemaJson) });

        AssertOutputConfigCarriesSchema(wire.Body!, EmailSchemaJson);
        using var document = JsonDocument.Parse(wire.Body!);
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(256, document.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(EmailAnswer, response.Text);
    }

    [Fact]
    public async Task BaseClient_AnthropicModeStreaming_ShouldSendTheSameOutputConfigFormat()
    {
        var wire = new WireRecorder();
        using var httpClient = new HttpClient(wire.StreamHandler());
        using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);

        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync(EmailPrompt, new ChatOptions { MaxOutputTokens = 1024, ResponseFormat = SchemaFormat(EmailSchemaJson) }))
        {
            text.Append(string.Concat(update.Contents.OfType<TextContent>().Select(c => c.Text)));
        }

        Assert.Equal(1, wire.Requests);
        Assert.False(wire.HadBetaHeader);
        AssertOutputConfigCarriesSchema(wire.Body!, EmailSchemaJson);
        using var document = JsonDocument.Parse(wire.Body!);
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(EmailAnswer, text.ToString());
    }

    public static TheoryData<string> LosslessSchemas => new()
    {
        // nested objects and arrays with required / additionalProperties at every level
        """{"type":"object","properties":{"patch":{"type":"object","properties":{"upserts":{"type":"array","items":{"type":"object","properties":{"kind":{"type":"integer"},"code":{"type":"string"}},"required":["kind","code"],"additionalProperties":false}},"remove":{"type":"array","items":{"type":"string"}}},"required":["upserts","remove"],"additionalProperties":false}},"required":["patch"],"additionalProperties":false}""",
        // $defs and $ref
        """{"$defs":{"address":{"type":"object","properties":{"street":{"type":"string"},"city":{"type":"string"}},"required":["street","city"],"additionalProperties":false}},"type":"object","properties":{"home":{"$ref":"#/$defs/address"},"work":{"$ref":"#/$defs/address"}},"required":["home","work"],"additionalProperties":false}""",
        // anyOf, enum, const, string formats, defaults with their original number text
        """{"type":"object","properties":{"status":{"type":"string","enum":["Pass","Fail","NeedsReview"]},"kind":{"const":"rule"},"when":{"anyOf":[{"type":"string","format":"date-time"},{"type":"null"}]},"ratio":{"type":"number","default":1.50},"contact":{"type":"string","format":"email"}},"required":["status","kind","when","ratio","contact"],"additionalProperties":false}""",
        // non-ASCII text in descriptions and property names
        """{"type":"object","description":"项目名称与说明","properties":{"项目名称":{"type":"string","description":"原文中的名称"}},"required":["项目名称"],"additionalProperties":false}""",
    };

    [Theory]
    [MemberData(nameof(LosslessSchemas))]
    public async Task BaseClient_AnthropicMode_ShouldKeepSchemaStructureWithoutRewriting(string schemaJson)
    {
        foreach (var streaming in new[] { false, true })
        {
            var wire = new WireRecorder();
            using var httpClient = new HttpClient(streaming ? wire.StreamHandler() : wire.Handler(EmailMessageResponse));
            using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);
            var options = new ChatOptions { MaxOutputTokens = 512, ResponseFormat = SchemaFormat(schemaJson) };

            if (streaming)
            {
                await foreach (var unused in client.GetStreamingResponseAsync(EmailPrompt, options)) { }
            }
            else
            {
                await client.GetResponseAsync(EmailPrompt, options);
            }

            AssertOutputConfigCarriesSchema(wire.Body!, schemaJson);                 // structure, required, additionalProperties, $ref/$defs, anyOf, enum, const untouched
            if (schemaJson.Contains("\"default\":1.50", StringComparison.Ordinal))
            {
                Assert.Contains("\"default\":1.50", wire.Body, StringComparison.Ordinal);   // numbers keep their original text
            }
            if (schemaJson.Contains("$ref", StringComparison.Ordinal))
            {
                Assert.Contains("\"$ref\":\"#/$defs/address\"", wire.Body, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("text")]
    public async Task BaseClient_AnthropicMode_NullOrTextResponseFormat_ShouldOmitOutputConfig(string kind)
    {
        foreach (var streaming in new[] { false, true })
        {
            var wire = new WireRecorder();
            using var httpClient = new HttpClient(streaming ? wire.StreamHandler() : wire.Handler(EmailMessageResponse));
            using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);
            var options = new ChatOptions { MaxOutputTokens = 64, ResponseFormat = kind == "text" ? ChatResponseFormat.Text : null };

            if (streaming)
            {
                await foreach (var unused in client.GetStreamingResponseAsync(EmailPrompt, options)) { }
            }
            else
            {
                await client.GetResponseAsync(EmailPrompt, options);
            }

            using var document = JsonDocument.Parse(wire.Body!);
            Assert.False(document.RootElement.TryGetProperty("output_config", out _));
            Assert.DoesNotContain("output_config", wire.Body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("base")]
    [InlineData("claude")]
    public async Task AnthropicMode_SchemalessJsonResponseFormat_ShouldFailBeforeAnyHttpRequest(string clientKind)
    {
        var wire = new WireRecorder();
        using var httpClient = new HttpClient(wire.Handler(EmailMessageResponse));
        using VllmBaseChatClient client = clientKind == "claude"
            ? new VllmClaudeChatClient("http://localhost:8000/v1", "test-key", "claude-test", httpClient, VllmApiMode.AnthropicMessages)
            : new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);
        var options = new ChatOptions { MaxOutputTokens = 64, ResponseFormat = ChatResponseFormat.Json };

        var normal = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(EmailPrompt, options));
        Assert.Contains("Anthropic", normal.Message, StringComparison.Ordinal);
        Assert.Contains("JSON schema", normal.Message, StringComparison.Ordinal);

        var streaming = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var unused in client.GetStreamingResponseAsync(EmailPrompt, options)) { }
        });
        Assert.Contains("Anthropic", streaming.Message, StringComparison.Ordinal);
        Assert.Contains("JSON schema", streaming.Message, StringComparison.Ordinal);

        Assert.Equal(0, wire.Requests);                                                          // nothing was sent
        Assert.Null(wire.Body);
    }

    [Fact]
    public async Task BaseClient_AnthropicMode_OutputConfigShouldCoexistWithToolsAndThinkingWithoutChangingThem()
    {
        var tools = new List<AITool> { AIFunctionFactory.Create((string city) => "sunny", "GetWeather", "Gets the weather for a city.") };

        async Task<string> PostAsync(ChatResponseFormat? format)
        {
            var wire = new WireRecorder();
            using var httpClient = new HttpClient(wire.Handler(EmailMessageResponse));
            using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.AnthropicMessages);
            await client.GetResponseAsync(EmailPrompt, new VllmChatOptions
            {
                ThinkingEnabled = false, MaxOutputTokens = 256, Tools = tools, ToolMode = ChatToolMode.RequireAny, ResponseFormat = format,
            });
            return wire.Body!;
        }

        var without = await PostAsync(null);
        var with = await PostAsync(SchemaFormat(EmailSchemaJson));

        AssertOutputConfigCarriesSchema(with, EmailSchemaJson);
        using var plain = JsonDocument.Parse(without);
        using var structured = JsonDocument.Parse(with);
        Assert.False(plain.RootElement.TryGetProperty("output_config", out _));
        foreach (var name in new[] { "model", "max_tokens", "messages", "tools", "tool_choice", "thinking", "stream" })
        {
            Assert.True(plain.RootElement.TryGetProperty(name, out var before), name);
            Assert.True(structured.RootElement.TryGetProperty(name, out var after), name);
            Assert.Equal(before.GetRawText(), after.GetRawText());                                // everything else is byte-identical
        }
        Assert.Equal("GetWeather", structured.RootElement.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Equal("object", structured.RootElement.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString());
        Assert.Equal("disabled", structured.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ChatCompletionsMode_ShouldKeepItsOpenAiResponseFormatAndNeverSendOutputConfig()
    {
        var wire = new WireRecorder();
        using var httpClient = new HttpClient(wire.Handler(
            """{"id":"x","object":"chat.completion","model":"claude-test","choices":[{"index":0,"message":{"role":"assistant","content":"{}"},"finish_reason":"stop"}]}"""));
        using var client = new TestVllmChatClient("http://localhost:8000/v1", "test-key", httpClient, VllmApiMode.ChatCompletions);

        await client.GetResponseAsync(EmailPrompt, new ChatOptions { MaxOutputTokens = 64, ResponseFormat = SchemaFormat(EmailSchemaJson) });

        using var document = JsonDocument.Parse(wire.Body!);
        Assert.False(document.RootElement.TryGetProperty("output_config", out _));
        Assert.True(document.RootElement.TryGetProperty("response_format", out _));
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
