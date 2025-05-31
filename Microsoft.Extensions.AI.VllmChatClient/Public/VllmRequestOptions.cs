namespace Microsoft.Extensions.AI;

#pragma warning disable IDE1006 // Naming Styles

internal sealed class VllmRequestOptions
{
    public float? temperature { get; set; }
    public float? top_p { get; set; }
    
}