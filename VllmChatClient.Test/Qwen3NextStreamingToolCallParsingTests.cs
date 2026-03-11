using Microsoft.Extensions.AI;
using System.Net;
using System.Text;

namespace VllmChatClient.Test;

public class Qwen3NextStreamingToolCallParsingTests
{
    [Fact]
    public async Task StreamingToolCalls_WithIndexedFragments_AreDetectedForQwen3Next()
    {
        const string streamPayload = """
data: {"choices":[{"delta":{"content":null,"reasoning_content":"The","role":"assistant"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"call_bd50","type":"function","function":{"name":"calculate","arguments":""}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"","type":"function","function":{"arguments":"{\"expression\": \"8"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"","type":"function","function":{"arguments":"8 + 9"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"","type":"function","function":{"arguments":"9"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"","type":"function","function":{"arguments":"\""}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":0,"id":"","type":"function","function":{"arguments":"}"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"call_6278","type":"function","function":{"name":"get_weather","arguments":""}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"","type":"function","function":{"arguments":"{\"city\": "}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"","type":"function","function":{"arguments":"\"Sh"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"","type":"function","function":{"arguments":"anghai"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"","type":"function","function":{"arguments":"\""}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":[{"index":1,"id":"","type":"function","function":{"arguments":"}"}}]},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: {"choices":[{"finish_reason":"tool_calls","delta":{"content":"","reasoning_content":null},"index":0,"logprobs":null}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-test"}
data: [DONE]
""";

        using var httpClient = new HttpClient(new FakeStreamingHandler(streamPayload));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "88 + 99，顺便查上海天气")
        };

        var calls = new List<FunctionCallContent>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, new ChatOptions()))
        {
            calls.AddRange(update.Contents.OfType<FunctionCallContent>());
        }

        Assert.Equal(2, calls.Count);
        Assert.Equal("calculate", calls[0].Name);
        Assert.Equal("get_weather", calls[1].Name);
        Assert.Equal("call_bd50", calls[0].CallId);
        Assert.Equal("call_6278", calls[1].CallId);
        Assert.Equal("88 + 99", calls[0].Arguments?["expression"]?.ToString());
        Assert.Equal("Shanghai", calls[1].Arguments?["city"]?.ToString());
    }

    [Fact]
    public async Task NonStreamingToolCalls_PreserveProviderToolCallId()
    {
        const string jsonResponse = """
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_nonstream_1",
            "type": "function",
            "function": {
              "name": "calculate",
              "arguments": "{\"expression\":\"88 + 99\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 5,
    "total_tokens": 15
  }
}
""";

        using var httpClient = new HttpClient(new FakeJsonHandler(jsonResponse));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "88 + 99")
        };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string expression) => expression, "calculate")]
        };

        var response = await client.GetResponseAsync(messages, options);
        var functionCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Equal("calculate", functionCall.Name);
        Assert.Equal("call_nonstream_1", functionCall.CallId);
        Assert.Equal("88 + 99", functionCall.Arguments?["expression"]?.ToString());
    }

    [Fact]
    public async Task NonStreamingToolCalls_WithEmptyFunctionName_ThrowsInvalidOperationException()
    {
        const string jsonResponse = """
{
  "id": "chatcmpl-test-empty-name",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_nonstream_empty",
            "type": "function",
            "function": {
              "arguments": "{\"path\":\"src/test.cs\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 5,
    "total_tokens": 15
  }
}
""";

        using var httpClient = new HttpClient(new FakeJsonHandler(jsonResponse));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "do something") };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string path) => path, "read_file")]
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(messages, options));
        Assert.Contains("empty function.name", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("call_nonstream_empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonStreamingToolCalls_WithEmptyFunctionName_RetriesOnceAndSucceeds()
    {
        const string badJsonResponse = """
{
  "id": "chatcmpl-bad",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_bad_empty_name",
            "type": "function",
            "function": {
              "arguments": "{\"expression\":\"1 + 1\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
}
""";

        const string goodJsonResponse = """
{
  "id": "chatcmpl-good",
  "object": "chat.completion",
  "created": 1771436119,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_good_1",
            "type": "function",
            "function": {
              "name": "calculate",
              "arguments": "{\"expression\":\"1 + 1\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
}
""";

        using var httpClient = new HttpClient(new SequenceJsonHandler([badJsonResponse, goodJsonResponse]));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "1+1") };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string expression) => expression, "calculate")]
        };

        var response = await client.GetResponseAsync(messages, options);
        var functionCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Equal("calculate", functionCall.Name);
        Assert.Equal("call_good_1", functionCall.CallId);
        Assert.Equal("1 + 1", functionCall.Arguments?["expression"]?.ToString());
    }

    [Fact]
    public async Task NonStreamingToolCalls_WithInvalidArgumentsJson_RetriesOnceAndSucceeds()
    {
        const string badJsonResponse = """
{
  "id": "chatcmpl-bad-args",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_bad_args",
            "type": "function",
            "function": {
              "name": "calculate",
              "arguments": "{\"expression\":"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
}
""";

        const string goodJsonResponse = """
{
  "id": "chatcmpl-good-args",
  "object": "chat.completion",
  "created": 1771436119,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "",
        "tool_calls": [
          {
            "id": "call_good_args",
            "type": "function",
            "function": {
              "name": "calculate",
              "arguments": "{\"expression\":\"2 + 2\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
}
""";

        using var httpClient = new HttpClient(new SequenceJsonHandler([badJsonResponse, goodJsonResponse]));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "2+2") };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string expression) => expression, "calculate")]
        };

        var response = await client.GetResponseAsync(messages, options);
        var functionCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Equal("calculate", functionCall.Name);
        Assert.Equal("call_good_args", functionCall.CallId);
        Assert.Equal("2 + 2", functionCall.Arguments?["expression"]?.ToString());
    }

    [Fact]
    public async Task NonStreamingToolCalls_WithToolCallTextBlock_AreParsedForQwen3Next()
    {
        const string jsonResponse = """
{
  "id": "chatcmpl-toolcall-text",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "<tool_call>\n{\n  \"name\": \"execute_code_task\",\n  \"arguments\": {\n    \"task_id\": \"TASK_FIX_001\"\n  }\n}\n</tool_call>"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        using var httpClient = new HttpClient(new FakeJsonHandler(jsonResponse));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "执行任务") };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string task_id) => task_id, "execute_code_task")]
        };

        var response = await client.GetResponseAsync(messages, options);
        var functionCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Equal("execute_code_task", functionCall.Name);
        Assert.Equal("TASK_FIX_001", functionCall.Arguments?["task_id"]?.ToString());
    }

    [Fact]
    public async Task StreamingToolCalls_WithToolCallTextBlock_AreParsedForQwen3Next()
    {
        const string streamPayload = """
data: {"choices":[{"delta":{"role":"assistant","content":"<tool_call>\n{\n  \"name\": \"execute_code_task\",\n"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-stream-text"}
data: {"choices":[{"delta":{"content":"  \"arguments\": {\n    \"task_id\": \"TASK_FIX_001\"\n  }\n}\n</tool_call>"},"finish_reason":null,"index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-stream-text"}
data: {"choices":[{"delta":{"content":""},"finish_reason":"stop","index":0}],"object":"chat.completion.chunk","usage":null,"created":1771436118,"model":"qwen3.5-plus","id":"chatcmpl-stream-text"}
data: [DONE]
""";

        using var httpClient = new HttpClient(new FakeStreamingHandler(streamPayload));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "执行任务") };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string task_id) => task_id, "execute_code_task")]
        };

        var calls = new List<FunctionCallContent>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, options))
        {
            calls.AddRange(update.Contents.OfType<FunctionCallContent>());
        }

        var functionCall = Assert.Single(calls);
        Assert.Equal("execute_code_task", functionCall.Name);
        Assert.Equal("TASK_FIX_001", functionCall.Arguments?["task_id"]?.ToString());
    }

    [Fact]
    public async Task NonStreamingJsonText_WithoutToolCalls_PreservesOuterBracesForQwen3Next()
    {
        const string jsonResponse = """
{
  "id": "chatcmpl-json-text",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "{\n  \"greeting\": \"你好，我是菲菲，有什么可以帮你的吗？\"\n}"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        using var httpClient = new HttpClient(new FakeJsonHandler(jsonResponse));
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "输出 JSON")], new VllmChatOptions
        {
            ThinkingEnabled = false
        });

        var text = response.Messages.Single().Text;
        Assert.NotNull(text);
        Assert.StartsWith("{", text.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("}", text.TrimEnd(), StringComparison.Ordinal);

        using var parsed = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal("你好，我是菲菲，有什么可以帮你的吗？", parsed.RootElement.GetProperty("greeting").GetString());
    }

    [Fact]
    public async Task NonStreamingQwen35_NoThinkingRequest_WritesTopLevelVllmFields()
    {
        const string jsonResponse = """
{
  "id": "chatcmpl-enable-thinking",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "qwen3.5-plus",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "{\"ok\":true}"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new CaptureJsonHandler(jsonResponse);
        using var httpClient = new HttpClient(handler);
        var client = new VllmQwen3NextChatClient("https://example.test/{0}/{1}", "fake-token", "qwen3.5-plus", httpClient);

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "输出 JSON")], new VllmChatOptions
        {
            ThinkingEnabled = false
        });

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("enable_thinking", out var enableThinking));
        Assert.False(enableThinking.GetBoolean());

        Assert.True(doc.RootElement.TryGetProperty("chat_template_kwargs", out var chatTemplateKwargs));
        Assert.True(chatTemplateKwargs.TryGetProperty("enable_thinking", out var nestedEnableThinking));
        Assert.False(nestedEnableThinking.GetBoolean());
    }

    private sealed class FakeStreamingHandler(string ssePayload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeJsonHandler(string jsonPayload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceJsonHandler(IReadOnlyList<string> jsonPayloads) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var idx = Interlocked.Increment(ref _index) - 1;
            var payload = idx < jsonPayloads.Count ? jsonPayloads[idx] : jsonPayloads[^1];
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CaptureJsonHandler(string jsonPayload) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
