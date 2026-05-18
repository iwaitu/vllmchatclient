using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmResponsesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("output")]
    public VllmResponsesOutputItem[]? Output { get; set; }

    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }

    [JsonPropertyName("usage")]
    public VllmResponsesUsage? Usage { get; set; }
}

internal sealed class VllmResponsesOutputItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public VllmResponsesContentPart[]? Content { get; set; }

    [JsonPropertyName("summary")]
    public VllmResponsesContentPart[]? Summary { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }
}

internal sealed class VllmResponsesContentPart
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class VllmResponsesUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
