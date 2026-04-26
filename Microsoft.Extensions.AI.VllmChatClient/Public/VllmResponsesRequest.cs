using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmResponsesRequest
{
    public required string Model { get; set; }
    public required object[] Input { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; }
    [JsonPropertyName("response_format")]
    public JsonElement? ResponseFormat { get; set; }
    public VllmReasoningOptions? Reasoning { get; set; }
    public float? Temperature { get; set; }
    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }
    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }
    [JsonPropertyName("stream_options")]
    public VllmStreamOptions? StreamOptions { get; set; }
    [JsonExtensionData]
    public Dictionary<string, object?>? ExtraBody { get; set; }
}

internal sealed class VllmResponsesMessageInput
{
    public required string Role { get; set; }
    public object? Content { get; set; }
}

internal sealed class VllmResponsesFunctionCallInput
{
    public string Type { get; set; } = "function_call";
    public required string Name { get; set; }
    public required string Arguments { get; set; }
    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }
}

internal sealed class VllmResponsesFunctionCallOutputInput
{
    public string Type { get; set; } = "function_call_output";
    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }
    public required string Output { get; set; }
}

internal sealed class VllmResponsesImageContentPart
{
    public string Type { get; set; } = "input_image";
    [JsonPropertyName("image_url")]
    public required string ImageUrl { get; set; }
}

internal sealed class VllmResponsesTextContentPart
{
    public string Type { get; set; } = "input_text";
    public required string Text { get; set; }
}
