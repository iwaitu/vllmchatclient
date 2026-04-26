using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmAnthropicMessagesResponse
{
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonProperty("role")]
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonProperty("model")]
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonProperty("content")]
    [JsonPropertyName("content")]
    public VllmAnthropicContentBlock[]? Content { get; set; }

    [JsonProperty("stop_reason")]
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonProperty("usage")]
    [JsonPropertyName("usage")]
    public VllmAnthropicUsage? Usage { get; set; }
}

internal sealed class VllmAnthropicContentBlock
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonProperty("text")]
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonProperty("thinking")]
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonProperty("input")]
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }
}

internal sealed class VllmAnthropicUsage
{
    [JsonProperty("input_tokens")]
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

internal sealed class VllmAnthropicStreamEvent
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonProperty("message")]
    [JsonPropertyName("message")]
    public VllmAnthropicMessagesResponse? Message { get; set; }

    [JsonProperty("index")]
    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonProperty("content_block")]
    [JsonPropertyName("content_block")]
    public VllmAnthropicContentBlock? ContentBlock { get; set; }

    [JsonProperty("delta")]
    [JsonPropertyName("delta")]
    public VllmAnthropicStreamDelta? Delta { get; set; }

    [JsonProperty("usage")]
    [JsonPropertyName("usage")]
    public VllmAnthropicUsage? Usage { get; set; }
}

internal sealed class VllmAnthropicStreamDelta
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonProperty("text")]
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonProperty("thinking")]
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonProperty("partial_json")]
    [JsonPropertyName("partial_json")]
    public string? PartialJson { get; set; }

    [JsonProperty("stop_reason")]
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}
