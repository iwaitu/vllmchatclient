using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public Choice[]? Choices { get; set; }

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

internal sealed class Choice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public VllmChatResponseMessage? Message { get; set; }

    [JsonPropertyName("logprobs")]
    public object? Logprobs { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("stop_reason")]
    public int? StopReason { get; set; }
}

/// <summary>
/// Chat Completions usage. The counters are kept as raw JSON so the shared accumulator can tell "absent" from an explicit zero and reject invalid values
/// with a fixed error; <see cref="PromptTokens"/> / <see cref="CompletionTokens"/> / <see cref="TotalTokens"/> stay as lenient int views for the clients
/// that still read them directly.
/// </summary>
internal sealed class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public JsonElement? RawPromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public JsonElement? RawTotalTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public JsonElement? RawCompletionTokens { get; set; }

    [JsonPropertyName("prompt_tokens_details")]
    public VllmPromptTokensDetails? PromptTokensDetails { get; set; }

    [JsonIgnore]
    public int PromptTokens => VllmUsageCounters.ToIntOrZero(RawPromptTokens);

    [JsonIgnore]
    public int TotalTokens => VllmUsageCounters.ToIntOrZero(RawTotalTokens);

    [JsonIgnore]
    public int CompletionTokens => VllmUsageCounters.ToIntOrZero(RawCompletionTokens);
}

internal sealed class VllmPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public JsonElement? CachedTokens { get; set; }
}
