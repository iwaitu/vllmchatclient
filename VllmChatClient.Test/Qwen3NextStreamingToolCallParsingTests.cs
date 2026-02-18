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
}
