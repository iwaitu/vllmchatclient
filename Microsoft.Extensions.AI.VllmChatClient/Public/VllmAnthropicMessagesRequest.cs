using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmAnthropicMessagesRequest
{
    public required string Model { get; set; }
    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; set; }
    public required VllmAnthropicMessage[] Messages { get; set; }
    public string? System { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmAnthropicTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")]
    public VllmAnthropicToolChoice? ToolChoice { get; set; }
    public VllmAnthropicThinkingOptions? Thinking { get; set; }
    public float? Temperature { get; set; }
    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }
}

internal sealed class VllmAnthropicMessage
{
    public required string Role { get; set; }
    public required object Content { get; set; }
}

internal sealed class VllmAnthropicTextBlock
{
    public string Type { get; set; } = "text";
    public required string Text { get; set; }
}

internal sealed class VllmAnthropicImageBlock
{
    public string Type { get; set; } = "image";
    public required VllmAnthropicImageSource Source { get; set; }
}

internal sealed class VllmAnthropicImageSource
{
    public string Type { get; set; } = "base64";
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "image/png";
    public required string Data { get; set; }
}

internal sealed class VllmAnthropicToolUseBlock
{
    public string Type { get; set; } = "tool_use";
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required JsonElement Input { get; set; }
}

internal sealed class VllmAnthropicToolResultBlock
{
    public string Type { get; set; } = "tool_result";
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; set; }
    public required string Content { get; set; }
}

internal sealed class VllmAnthropicTool
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    [JsonPropertyName("input_schema")]
    public required VllmFunctionToolParameters InputSchema { get; set; }
}

internal sealed class VllmAnthropicToolChoice
{
    public required string Type { get; set; }
    public string? Name { get; set; }
}

internal sealed class VllmAnthropicThinkingOptions
{
    public required string Type { get; set; }
    [JsonPropertyName("budget_tokens")]
    public int? BudgetTokens { get; set; }
}
