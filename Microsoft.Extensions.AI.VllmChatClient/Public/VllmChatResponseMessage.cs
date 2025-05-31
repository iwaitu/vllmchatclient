using Newtonsoft.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmChatResponseMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }

    [JsonProperty("reasoning_content")]
    public object ReasoningContent { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; }

    [JsonProperty("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}