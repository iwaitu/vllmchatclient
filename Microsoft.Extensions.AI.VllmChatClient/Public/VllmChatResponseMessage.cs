using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponseMessage
{
    [JsonProperty("role")]
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonProperty("reasoning_content")]
    [JsonPropertyName("reasoning_content")]
    public object ReasoningContent { get; set; }
    
    // 基于 Python 脚本输出添加的字段
    [JsonProperty("reasoning")]
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; }
    
    [JsonProperty("reasoning_details")]
    [JsonPropertyName("reasoning_details")]
    public VllmReasoningDetail[]? ReasoningDetails { get; set; }
    
    [JsonProperty("refusal")]
    [JsonPropertyName("refusal")]
    public string Refusal { get; set; }

    [JsonProperty("content")]
    [JsonPropertyName("content")]
    public string Content { get; set; }

    [JsonProperty("tool_calls")]
    [JsonPropertyName("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}