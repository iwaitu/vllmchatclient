using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI.VllmChatClient.Gemma
{
    /// <summary>
    /// Gemini API 请求模型
    /// </summary>
    internal class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }

        [JsonPropertyName("tools")]
        public GeminiTool[]? Tools { get; set; }
    }

    /// <summary>
    /// Gemini 内容模型
    /// </summary>
    internal class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    /// <summary>
    /// Gemini 内容部分
    /// </summary>
    internal class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("thoughtSignature")]
        public string? ThoughtSignature { get; set; }

        [JsonPropertyName("inlineData")]
        public GeminiInlineData? InlineData { get; set; }

        [JsonPropertyName("functionCall")]
        public GeminiFunctionCall? FunctionCall { get; set; }

        [JsonPropertyName("functionResponse")]
        public GeminiFunctionResponse? FunctionResponse { get; set; }
    }

    /// <summary>
    /// Gemini 内联数据（用于图片等）
    /// </summary>
    internal class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "";

        [JsonPropertyName("data")]
        public string Data { get; set; } = "";
    }

    /// <summary>
    /// Gemini 函数调用
    /// </summary>
    internal class GeminiFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("args")]
        public Dictionary<string, object?>? Args { get; set; }
    }

    /// <summary>
    /// Gemini 函数响应
    /// </summary>
    internal class GeminiFunctionResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("response")]
        public Dictionary<string, object?>? Response { get; set; }
    }

    /// <summary>
    /// Gemini 生成配置
    /// </summary>
    internal class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }

        [JsonPropertyName("topP")]
        public float? TopP { get; set; }

        [JsonPropertyName("thinkingConfig")]
        public GeminiThinkingConfig? ThinkingConfig { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string? ResponseMimeType { get; set; }

        [JsonPropertyName("responseSchema")]
        public object? ResponseSchema { get; set; }
    }

    /// <summary>
    /// Gemini 思考配置
    /// </summary>
    internal class GeminiThinkingConfig
    {
        [JsonPropertyName("thinkingLevel")]
        public string ThinkingLevel { get; set; } = "high";
    }

    /// <summary>
    /// Gemini 工具定义
    /// </summary>
    internal class GeminiTool
    {
        [JsonPropertyName("functionDeclarations")]
        public GeminiFunctionDeclaration[]? FunctionDeclarations { get; set; }
    }

    /// <summary>
    /// Gemini 函数声明
    /// </summary>
    internal class GeminiFunctionDeclaration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    /// <summary>
    /// Gemini API 响应模型
    /// </summary>
    internal class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    /// <summary>
    /// Gemini 候选响应
    /// </summary>
    internal class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    /// <summary>
    /// Gemini 使用元数据
    /// </summary>
    internal class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }
}
