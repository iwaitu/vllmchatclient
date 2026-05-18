using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("reasoning_content")]
    public object? ReasoningContent { get; set; }
    
    // 基于 Python 脚本输出添加的字段
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
    
    [JsonPropertyName("reasoning_details")]
    public VllmReasoningDetail[]? ReasoningDetails { get; set; }
    
    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}
