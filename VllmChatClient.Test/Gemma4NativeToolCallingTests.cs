using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class Gemma4NativeToolCallingTests
{
    [Fact]
    public async Task NativeEndpoint_ResponseText_ParsesGemma4ToolCallMarkup()
    {
        const string responseJson = """
{
  "id": "chatcmpl-native-1",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "<|tool_call>call:GetWeather{city:<|\"|>南宁<|\"|>}<tool_call|><|tool_response>"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        using var httpClient = new HttpClient(new SequenceHandler([responseJson]));
        var client = new VllmGemma4ChatClient("https://example.test/v1", "fake-token", httpClient: httpClient);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "南宁天气如何？")],
            new ChatOptions
            {
                Tools = [AIFunctionFactory.Create((string city) => city, "GetWeather")]
            });

        var functionCall = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("GetWeather", functionCall.Name);
        Assert.Equal("南宁", functionCall.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task NativeEndpoint_ToolResult_UsesOpenAiCompatibleFollowUpMessages()
    {
        const string toolCallResponseJson = """
{
  "id": "chatcmpl-native-1",
  "object": "chat.completion",
  "created": 1771436118,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "<|tool_call>call:GetWeather{city:<|\"|>南宁<|\"|>}<tool_call|><|tool_response>"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        const string finalResponseJson = """
{
  "id": "chatcmpl-native-2",
  "object": "chat.completion",
  "created": 1771436119,
  "model": "google/gemma-4-31b-it",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "南宁天气晴朗。"
      },
      "finish_reason": "stop"
    }
  ]
}
""";

        var handler = new SequenceHandler([toolCallResponseJson, finalResponseJson]);
        using var httpClient = new HttpClient(handler);
        var client = new VllmGemma4ChatClient("https://example.test/v1", "fake-token", httpClient: httpClient);

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string city) => city, "GetWeather")]
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "南宁天气如何？")
        };

        var toolCallResponse = await client.GetResponseAsync(messages, options);
        var functionCall = toolCallResponse.Messages.Single().Contents.OfType<FunctionCallContent>().Single();

        messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(functionCall.CallId, new { temperature = 30, weather = "sunny" })]));

        var finalResponse = await client.GetResponseAsync(messages, options);
        Assert.Equal("南宁天气晴朗。", finalResponse.Text);

        Assert.True(handler.RequestBodies.Count >= 2);
        using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var requestMessages = secondRequest.RootElement.GetProperty("messages");

        Assert.Equal(3, requestMessages.GetArrayLength());
        var assistantMessage = requestMessages[1];
        Assert.Equal("assistant", assistantMessage.GetProperty("role").GetString());
        Assert.True(assistantMessage.TryGetProperty("tool_calls", out var toolCalls));
        Assert.False(assistantMessage.TryGetProperty("tool_responses", out _));
        Assert.Equal("GetWeather", toolCalls[0].GetProperty("function").GetProperty("name").GetString());

        var toolMessage = requestMessages[2];
        Assert.Equal("tool", toolMessage.GetProperty("role").GetString());
        Assert.Equal(functionCall.CallId, toolMessage.GetProperty("tool_call_id").GetString());
        using var toolResult = JsonDocument.Parse(toolMessage.GetProperty("content").GetString()!);
        Assert.Equal(30, toolResult.RootElement.GetProperty("temperature").GetInt32());
        Assert.Equal("sunny", toolResult.RootElement.GetProperty("weather").GetString());
    }

    private sealed class SequenceHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            var response = responses[Math.Min(_index, responses.Count - 1)];
            _index++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
