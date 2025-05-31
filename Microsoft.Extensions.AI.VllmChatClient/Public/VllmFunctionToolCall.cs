using Newtonsoft.Json;
using System.Text.Json;

namespace Microsoft.Extensions.AI;



internal sealed class VllmFunctionToolCall
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("arguments")]
    public string? Arguments { get; set; }
}