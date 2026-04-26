using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmResponsesResponse
{
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonProperty("object")]
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonProperty("created_at")]
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonProperty("model")]
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonProperty("status")]
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonProperty("output")]
    [JsonPropertyName("output")]
    public VllmResponsesOutputItem[]? Output { get; set; }

    [JsonProperty("output_text")]
    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }

    [JsonProperty("usage")]
    [JsonPropertyName("usage")]
    public VllmResponsesUsage? Usage { get; set; }
}

internal sealed class VllmResponsesOutputItem
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

    [JsonProperty("content")]
    [JsonPropertyName("content")]
    public VllmResponsesContentPart[]? Content { get; set; }

    [JsonProperty("summary")]
    [JsonPropertyName("summary")]
    public VllmResponsesContentPart[]? Summary { get; set; }

    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonProperty("arguments")]
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonProperty("call_id")]
    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }
}

internal sealed class VllmResponsesContentPart
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonProperty("text")]
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class VllmResponsesUsage
{
    [JsonProperty("input_tokens")]
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonProperty("total_tokens")]
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
