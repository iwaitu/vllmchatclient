using System.Text.Json;

namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionResultContent
{
    public string? CallId { get; set; }
    public JsonElement Result { get; set; }
}