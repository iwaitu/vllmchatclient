using Newtonsoft.Json;

namespace Microsoft.Extensions.AI;
internal class VllmChatStreamResponse
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("object")]
    public string Object { get; set; }

    [JsonProperty("created")]
    public long Created { get; set; }

    [JsonProperty("model")]
    public string Model { get; set; }

    [JsonProperty("choices")]
    public List<ChoiceChunk> Choices { get; set; }
}

internal class ChoiceChunk
{
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("delta")]
    public Delta Delta { get; set; }

    [JsonProperty("logprobs")]
    public object Logprobs { get; set; }

    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
}

internal class Delta
{
    [JsonProperty("role")]
    public string Role { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; }

    [JsonProperty("reasoning_content")]
    public string ReasoningContent { get; set; }
    [JsonProperty("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}