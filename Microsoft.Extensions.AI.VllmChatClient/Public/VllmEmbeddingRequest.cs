namespace Microsoft.Extensions.AI;

internal sealed class VllmEmbeddingRequest
{
    public required string Model { get; set; }
    public required string[] Input { get; set; }
    public VllmRequestOptions? Options { get; set; }
    public bool? Truncate { get; set; }
    public long? KeepAlive { get; set; }
}