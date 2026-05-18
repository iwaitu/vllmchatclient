using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

// 推理详情项
internal sealed class VllmReasoningDetail
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";
    
    [JsonPropertyName("index")]
    public int Index { get; set; }
}

internal class VllmChatStreamResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<ChoiceChunk>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
    
    // GPT-OSS Responses API 支持
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("sequence_number")]
    public int? SequenceNumber { get; set; }
    
    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }
    
    [JsonPropertyName("output_index")]
    public int? OutputIndex { get; set; }
    
    [JsonPropertyName("content_index")]
    public int? ContentIndex { get; set; }
    
    [JsonPropertyName("delta")]
    public string? Delta { get; set; }
    
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("response")]
    public VllmResponsesResponse? Response { get; set; }

    [JsonPropertyName("item")]
    public VllmResponsesOutputItem? Item { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }
}

internal class ChoiceChunk
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public Delta? Delta { get; set; }

    [JsonPropertyName("logprobs")]
    public object? Logprobs { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class Delta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
    
    // 基于 Python 脚本输出添加的字段
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
    
    [JsonPropertyName("reasoning_details")]
    public VllmReasoningDetail[]? ReasoningDetails { get; set; }
    
    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }
    
    [JsonPropertyName("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }
}
