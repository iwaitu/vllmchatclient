using Newtonsoft.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponse
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("object")]
    public string? Object { get; set; }

    [JsonProperty("created")]
    public long Created { get; set; }

    [JsonProperty("model")]
    public string? Model { get; set; }

    [JsonProperty("choices")]
    public Choice[]? Choices { get; set; }

    [JsonProperty("usage")]
    public Usage? Usage { get; set; }
}

internal sealed class Choice
{
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("message")]
    public VllmChatResponseMessage? Message { get; set; }

    [JsonProperty("logprobs")]
    public object? Logprobs { get; set; }

    [JsonProperty("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonProperty("stop_reason")]
    public int? StopReason { get; set; }
}

internal sealed class Usage
{
    [JsonProperty("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonProperty("completion_tokens")]
    public int CompletionTokens { get; set; }
}