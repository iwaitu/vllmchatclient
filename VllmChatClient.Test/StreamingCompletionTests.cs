using Microsoft.Extensions.AI;
using System.Net;
using System.Text;

namespace VllmChatClient.Test;

public class StreamingCompletionTests
{
    [Theory]
    [InlineData(VllmApiMode.ChatCompletions, false)]
    [InlineData(VllmApiMode.ChatCompletions, true)]
    [InlineData(VllmApiMode.Responses, false)]
    [InlineData(VllmApiMode.Responses, true)]
    [InlineData(VllmApiMode.AnthropicMessages, false)]
    [InlineData(VllmApiMode.AnthropicMessages, true)]
    public async Task ParallelToolCalls_CompleteOnlyAfterAllContentAndUsage(VllmApiMode mode, bool qwen)
    {
        var updates = await CollectAsync(mode, qwen, Payload(mode));
        var calls = updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>().ToArray();
        Assert.Equal(new[] { "call-a", "call-b" }, calls.Select(call => call.CallId));
        Assert.Equal(new[] { "first", "second" }, calls.Select(call => call.Name));
        Assert.All(updates.Where(update => update.Contents.OfType<FunctionCallContent>().Any()), update => Assert.Null(update.FinishReason));
        var completion = Assert.Single(updates, update => update.FinishReason.HasValue);
        Assert.Same(updates[^1], completion);
        Assert.Empty(completion.Contents);
        Assert.Equal(ChatFinishReason.ToolCalls, completion.FinishReason);
        Assert.Contains(updates, update => update is UsageChatResponseUpdate);
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task ToolCallWithTokenLimit_PreservesTheUnsafeProviderFinishReason(VllmApiMode mode)
    {
        var payload = Payload(mode)
            .Replace("\"finish_reason\":\"tool_calls\"", "\"finish_reason\":\"length\"")
            .Replace("\"status\":\"completed\"", "\"status\":\"incomplete\"")
            .Replace("\"stop_reason\":\"tool_use\"", "\"stop_reason\":\"max_tokens\"");
        var updates = await CollectAsync(mode, false, payload);
        Assert.Equal(ChatFinishReason.Length, Assert.Single(updates, update => update.FinishReason.HasValue).FinishReason);
        Assert.Equal(2, updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>().Count());
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(VllmApiMode mode, bool qwen, string payload)
    {
        using var http = new HttpClient(new StreamingHandler(payload));
        using VllmBaseChatClient client = qwen
            ? new VllmQwen3NextChatClient("https://example.test/v1", modelId: "qwen3.8-27b", httpClient: http, apiMode: mode)
            : new BaseClient(http, mode);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Call both tools.")]))
            updates.Add(update);
        return updates;
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task ContentAfterProviderCompletion_IsRejected(VllmApiMode mode)
    {
        var late = mode switch
        {
            VllmApiMode.Responses => "data: {\"type\":\"response.output_text.delta\",\"delta\":\"late\"}\n\n",
            VllmApiMode.AnthropicMessages => "data: {\"type\":\"content_block_delta\",\"index\":2,\"delta\":{\"type\":\"text_delta\",\"text\":\"late\"}}\n\n",
            _ => "data: {\"choices\":[{\"delta\":{\"content\":\"late\"},\"finish_reason\":null}]}\n\n"
        };
        var payload = Payload(mode).Replace("data: [DONE]", late + "data: [DONE]");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(mode, false, payload));
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    [InlineData(VllmApiMode.AnthropicMessages)]
    public async Task ConflictingProviderCompletion_IsRejected(VllmApiMode mode)
    {
        var conflicting = mode switch
        {
            VllmApiMode.Responses => "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"incomplete\"}}\n\n",
            VllmApiMode.AnthropicMessages => "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}\n\n",
            _ => "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\n"
        };
        var payload = Payload(mode).Replace("data: [DONE]", conflicting + "data: [DONE]");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(mode, false, payload));
    }

    private static string Payload(VllmApiMode mode)
        => mode switch
        {
            VllmApiMode.Responses => """
                data: {"type":"response.output_item.added","output_index":0,"item":{"id":"item-a","type":"function_call","call_id":"call-a","name":"first","arguments":""}}

                data: {"type":"response.function_call_arguments.delta","item_id":"item-a","output_index":0,"delta":"{}"}

                data: {"type":"response.output_item.done","output_index":0,"item":{"id":"item-a","type":"function_call","call_id":"call-a","name":"first","arguments":"{}"}}

                data: {"type":"response.output_item.added","output_index":1,"item":{"id":"item-b","type":"function_call","call_id":"call-b","name":"second","arguments":""}}

                data: {"type":"response.function_call_arguments.delta","item_id":"item-b","output_index":1,"delta":"{}"}

                data: {"type":"response.output_item.done","output_index":1,"item":{"id":"item-b","type":"function_call","call_id":"call-b","name":"second","arguments":"{}"}}

                data: {"type":"response.completed","response":{"id":"response-review","status":"completed","usage":{"input_tokens":1,"output_tokens":2,"total_tokens":3}}}

                data: [DONE]

                """,
            VllmApiMode.AnthropicMessages => """
                data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call-a","name":"first","input":{}}}

                data: {"type":"content_block_stop","index":0}

                data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"call-b","name":"second","input":{}}}

                data: {"type":"content_block_stop","index":1}

                data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":2}}

                data: {"type":"message_stop"}

                data: [DONE]

                """,
            _ => """
                data: {"id":"chat-review","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call-a","type":"function","function":{"name":"first","arguments":"{}"}}]},"finish_reason":null}]}

                data: {"id":"chat-review","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"id":"call-b","type":"function","function":{"name":"second","arguments":"{}"}}]},"finish_reason":null}]}

                data: {"id":"chat-review","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

                data: {"id":"chat-review","choices":[],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}

                data: [DONE]

                """
        };

    private sealed class BaseClient(HttpClient http, VllmApiMode mode) : VllmBaseChatClient("https://example.test/v1", token: null, modelId: "test-model", httpClient: http, apiMode: mode);
    private sealed class StreamingHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") });
    }
}
