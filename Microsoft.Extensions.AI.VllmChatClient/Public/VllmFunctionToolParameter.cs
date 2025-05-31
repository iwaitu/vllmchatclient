using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionToolParameter
{
    public string? Type { get; set; }
    public string? Description { get; set; }
    public IEnumerable<string>? Enum { get; set; }
}