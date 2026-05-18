using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmToolCall
{
    [JsonPropertyName("extra_content")]
    public VllmToolCallExtraContent? ExtraContent { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonPropertyName("index")]
    public int? Index { get; set; }
    [JsonPropertyName("function")]
    public VllmFunctionToolCall? Function { get; set; }
}

internal sealed class VllmToolCallExtraContent
{
    [JsonPropertyName("google")]
    public VllmToolCallGoogleExtraContent? Google { get; set; }
}

internal sealed class VllmToolCallGoogleExtraContent
{
    [JsonPropertyName("thought_signature")]
    public string? ThoughtSignature { get; set; }
}
