namespace Microsoft.Extensions.AI;

#pragma warning disable IDE1006 // Naming Styles

internal sealed class VllmRequestOptions
{
    public float? temperature { get; set; }
    public float? top_p { get; set; }
    
    // 添加 extra_body 支持以尝试启用推理功能
    public Dictionary<string, object?>? extra_body { get; set; }
}