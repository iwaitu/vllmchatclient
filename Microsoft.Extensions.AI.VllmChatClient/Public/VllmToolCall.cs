using Newtonsoft.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmToolCall
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonProperty("index")]
    public int? Index { get; set; }
    [JsonProperty("function")]
    public VllmFunctionToolCall? Function { get; set; }
}