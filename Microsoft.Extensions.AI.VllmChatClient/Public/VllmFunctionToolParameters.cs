using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionToolParameters
{
    public string Type { get; set; } = "object";
    public required IDictionary<string, JsonElement> Properties { get; set; }
    public IList<string>? Required { get; set; }
}