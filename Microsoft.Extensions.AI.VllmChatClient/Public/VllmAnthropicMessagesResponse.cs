using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmAnthropicMessagesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("content")]
    public VllmAnthropicContentBlock[]? Content { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public VllmAnthropicUsage? Usage { get; set; }
}

internal sealed class VllmAnthropicContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }
}

/// <summary>
/// Anthropic Messages usage. Counters are raw JSON so "absent" and explicit zero stay distinguishable and invalid values are rejected by the shared accumulator.
/// Raw <c>input_tokens</c> excludes the cache parts.
/// </summary>
internal sealed class VllmAnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public JsonElement? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public JsonElement? OutputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public JsonElement? CacheReadInputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public JsonElement? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_creation")]
    public VllmAnthropicCacheCreationUsage? CacheCreation { get; set; }
}

internal sealed class VllmAnthropicCacheCreationUsage
{
    [JsonPropertyName("ephemeral_5m_input_tokens")]
    public JsonElement? Ephemeral5mInputTokens { get; set; }

    [JsonPropertyName("ephemeral_1h_input_tokens")]
    public JsonElement? Ephemeral1hInputTokens { get; set; }
}

internal sealed class VllmAnthropicStreamEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public VllmAnthropicMessagesResponse? Message { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("content_block")]
    public VllmAnthropicContentBlock? ContentBlock { get; set; }

    [JsonPropertyName("delta")]
    public VllmAnthropicStreamDelta? Delta { get; set; }

    [JsonPropertyName("usage")]
    public VllmAnthropicUsage? Usage { get; set; }
}

internal sealed class VllmAnthropicStreamDelta
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("partial_json")]
    public string? PartialJson { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}
