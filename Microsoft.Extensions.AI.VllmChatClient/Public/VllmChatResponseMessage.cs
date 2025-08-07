using Newtonsoft.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponseMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }

    [JsonProperty("reasoning_content")]
    public object ReasoningContent { get; set; }
    
    // 基于 Python 脚本输出添加的字段
    [JsonProperty("reasoning")]
    public string Reasoning { get; set; }
    
    [JsonProperty("reasoning_details")]
    public object[] ReasoningDetails { get; set; }
    
    [JsonProperty("refusal")]
    public string Refusal { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; }

    [JsonProperty("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}