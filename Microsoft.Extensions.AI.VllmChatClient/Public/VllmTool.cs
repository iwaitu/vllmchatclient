namespace Microsoft.Extensions.AI;

internal sealed class VllmTool
{
    public required string Type { get; set; }
    public required VllmFunctionTool Function { get; set; }
}

internal sealed class VllmToolResponse
{
    public required string Name { get; set; }
    public object? Response { get; set; }
}