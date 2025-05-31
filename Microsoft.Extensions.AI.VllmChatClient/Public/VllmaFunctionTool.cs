namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionTool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required VllmFunctionToolParameters Parameters { get; set; }
}