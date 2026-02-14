
using System.Text.Json;

namespace Microsoft.Extensions.AI;


internal sealed class VllmOpenAIChatRequest
{
    public required string Model { get; set; }
    public required VllmOpenAIChatRequestMessage[] Messages { get; set; }
    public JsonElement? Format { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmTool>? Tools { get; set; }
    public VllmThinkingOptions? Thinking { get; set; }
    public bool? EnableThinking { get; set; }
    public VllmReasoningOptions? Reasoning { get; set; }
    public int? MaxTokens { get; set; }
    public VllmRequestOptions? Options { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; }
}

internal sealed class VllmThinkingOptions
{
    public required string Type { get; set; }
}

/// <summary>
/// Claude/OpenRouter 推理选项，序列化为 reasoning: {effort: "high"} 或 reasoning: {enabled: true}
/// </summary>
internal sealed class VllmReasoningOptions
{
    public bool? Enabled { get; set; }
    public string? Effort { get; set; }
    public bool? Exclude { get; set; }
}

internal sealed class VllmChatRequest
{
    public required string Model { get; set; }
    public required VllmChatRequestMessage[] Messages { get; set; }
    public JsonElement? Format { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmTool>? Tools { get; set; }
    public VllmRequestOptions? Options { get; set; }
}