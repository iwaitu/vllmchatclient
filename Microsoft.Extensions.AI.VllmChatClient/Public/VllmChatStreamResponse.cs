using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

// 推理详情项
internal sealed class VllmReasoningDetail
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonProperty("text")]
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonProperty("format")]
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";
    
    [JsonProperty("index")]
    [JsonPropertyName("index")]
    public int Index { get; set; }
}

internal class VllmChatStreamResponse
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("object")]
    public string? Object { get; set; }

    [JsonProperty("created")]
    public long Created { get; set; }

    [JsonProperty("model")]
    public string? Model { get; set; }

    [JsonProperty("choices")]
    public List<ChoiceChunk>? Choices { get; set; }
    
    // GPT-OSS Responses API 支持
    [JsonProperty("type")]
    public string? Type { get; set; }
    
    [JsonProperty("sequence_number")]
    public int? SequenceNumber { get; set; }
    
    [JsonProperty("item_id")]
    public string? ItemId { get; set; }
    
    [JsonProperty("output_index")]
    public int? OutputIndex { get; set; }
    
    [JsonProperty("content_index")]
    public int? ContentIndex { get; set; }
    
    [JsonProperty("delta")]
    public string? Delta { get; set; }
    
    [JsonProperty("text")]
    public string? Text { get; set; }
}

internal class ChoiceChunk
{
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("delta")]
    public Delta? Delta { get; set; }

    [JsonProperty("logprobs")]
    public object? Logprobs { get; set; }

    [JsonProperty("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class Delta
{
    [JsonProperty("role")]
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonProperty("content")]
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonProperty("reasoning_content")]
    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
    
    // 基于 Python 脚本输出添加的字段
    [JsonProperty("reasoning")]
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
    
    [JsonProperty("reasoning_details")]
    [JsonPropertyName("reasoning_details")]
    public VllmReasoningDetail[]? ReasoningDetails { get; set; }
    
    [JsonProperty("refusal")]
    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }
    
    [JsonProperty("tool_calls")]
    [JsonPropertyName("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}