using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VllmChatRequest))]
[JsonSerializable(typeof(VllmChatRequestMessage))]
[JsonSerializable(typeof(VllmOpenAIChatRequest))]
[JsonSerializable(typeof(VllmOpenAIChatRequestMessage))]
[JsonSerializable(typeof(VllmChatResponse))]
[JsonSerializable(typeof(VllmChatResponseMessage))]
[JsonSerializable(typeof(VllmFunctionCallContent))]
[JsonSerializable(typeof(VllmFunctionResultContent))]
[JsonSerializable(typeof(VllmFunctionTool))]
[JsonSerializable(typeof(VllmFunctionToolCall))]
[JsonSerializable(typeof(VllmFunctionToolParameter))]
[JsonSerializable(typeof(VllmFunctionToolParameters))]
[JsonSerializable(typeof(VllmRequestOptions))]
[JsonSerializable(typeof(VllmTool))]
[JsonSerializable(typeof(VllmToolCall))]
[JsonSerializable(typeof(VllmEmbeddingRequest))]
[JsonSerializable(typeof(VllmEmbeddingResponse))]
[JsonSerializable(typeof(VllmChatStreamResponse))]
[JsonSerializable(typeof(VllmReasoningDetail))]
[JsonSerializable(typeof(VllmReasoningOptions))]
internal sealed partial class JsonContext : JsonSerializerContext;