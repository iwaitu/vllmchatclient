using System.Text.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionCallContent
{
    public string? CallId { get; set; }
    public string? Name { get; set; }
    public JsonElement Arguments { get; set; }
}
