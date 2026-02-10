
using System.Text.Json;

namespace Microsoft.Extensions.AI;


internal sealed class VllmOpenAIChatRequest
{
    public required string Model { get; set; }
    public required VllmOpenAIChatRequestMessage[] Messages { get; set; }
    public JsonElement? Format { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmTool>? Tools { get; set; }
    public VllmThinkingOptions? Thinking { get; set; }
    public bool? EnableThinking { get; set; }
    public VllmRequestOptions? Options { get; set; }
}

internal sealed class VllmThinkingOptions
{
    public required string Type { get; set; }
}

internal sealed class VllmChatRequest
{
    public required string Model { get; set; }
    public required VllmChatRequestMessage[] Messages { get; set; }
    public JsonElement? Format { get; set; }
    public bool Stream { get; set; }
    public IEnumerable<VllmTool>? Tools { get; set; }
    public VllmRequestOptions? Options { get; set; }
}