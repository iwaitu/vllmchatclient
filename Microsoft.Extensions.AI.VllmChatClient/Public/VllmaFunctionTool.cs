namespace Microsoft.Extensions.AI;

internal sealed class VllmFunctionTool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required VllmFunctionToolParameters Parameters { get; set; }

    /// <summary>
    /// Tool-level strict flag. Omitted from the wire unless the caller explicitly sets
    /// <see cref="VllmToolStrictness.AdditionalPropertyKey"/> on the <see cref="AIFunction"/>.
    /// </summary>
    public bool? Strict { get; set; }
}

internal static class VllmToolStrictness
{
    /// <summary>Same key Microsoft.Extensions.AI.OpenAI reads from <c>AIFunction.AdditionalProperties</c>.</summary>
    public const string AdditionalPropertyKey = "strictJsonSchema";

    public static bool? Read(AIFunction function)
    {
        if (function.AdditionalProperties is not { } properties ||
            !properties.TryGetValue(AdditionalPropertyKey, out var value))
        {
            return null;
        }

        return value switch
        {
            bool flag => flag,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True } => true,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.False } => false,
            _ => null,
        };
    }
}