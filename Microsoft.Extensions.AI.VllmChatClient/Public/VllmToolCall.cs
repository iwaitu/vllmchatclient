using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmToolCall
{
    [JsonProperty("extra_content")]
    [JsonPropertyName("extra_content")]
    public VllmToolCallExtraContent? ExtraContent { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonProperty("index")]
    public int? Index { get; set; }
    [JsonProperty("function")]
    public VllmFunctionToolCall? Function { get; set; }
}

internal sealed class VllmToolCallExtraContent
{
    [JsonProperty("google")]
    [JsonPropertyName("google")]
    public VllmToolCallGoogleExtraContent? Google { get; set; }
}

internal sealed class VllmToolCallGoogleExtraContent
{
    [JsonProperty("thought_signature")]
    [JsonPropertyName("thought_signature")]
    public string? ThoughtSignature { get; set; }
}
