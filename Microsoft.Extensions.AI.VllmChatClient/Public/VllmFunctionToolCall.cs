using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;



internal sealed class VllmFunctionToolCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}
