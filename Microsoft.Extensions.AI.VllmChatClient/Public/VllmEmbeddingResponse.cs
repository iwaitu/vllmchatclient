﻿using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

internal sealed class VllmEmbeddingResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    [JsonPropertyName("embeddings")]
    public float[][]? Embeddings { get; set; }
    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }
    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; set; }
    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }
    public string? Error { get; set; }
}