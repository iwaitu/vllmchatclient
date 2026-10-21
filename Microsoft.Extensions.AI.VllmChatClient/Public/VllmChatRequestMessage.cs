
namespace Microsoft.Extensions.AI;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class RawJsonStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() ?? "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            // For OpenAI-compatible chat.completions, `content` may be:
            // - string
            // - array of content-part objects
            // but NOT a single JSON object.
            // So only emit raw JSON when the payload is an array; objects stay as plain strings.
            var trimmed = value.TrimStart();
            if (trimmed.StartsWith("["))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(value))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            doc.WriteTo(writer);
                            return;
                        }
                    }
                }
                catch (JsonException)
                {
                    // 如果解析失败，则说明不是合法的 JSON，走普通写入流程
                }
            }
        }
        // 普通写入字符串
        writer.WriteStringValue(value);
    }
}
internal class VllmOpenAIChatRequestMessage
{
    public string Role { get; set; } = default!;
    /// <summary>
    /// Plain message text. Always serialized as a JSON string, even when the text itself
    /// is a JSON array (for example a model answer echoed back in history).
    /// </summary>
    [JsonIgnore]
    public string? Content { get; set; } = default!;
    /// <summary>
    /// Explicit OpenAI content parts built by the client from typed media/text contents.
    /// When set, it is the wire <c>content</c> array and <see cref="Content"/> only holds the text view.
    /// </summary>
    [JsonIgnore]
    public JsonElement[]? ContentParts { get; set; }
    [JsonPropertyName("content")]
    public VllmOpenAIMessageContent? SerializedContent
    {
        get => ContentParts is { } parts
            ? new(Content ?? string.Empty, parts)
            : Content is null ? null : new(Content);
        set
        {
            Content = value?.Text;
            ContentParts = value?.Parts;
        }
    }
    /// <summary>
    /// Text for adapters (Responses, Anthropic) that have no typed mapping for chat-completions
    /// content parts; they keep receiving the serialized parts exactly as before 2.0.27.
    /// </summary>
    [JsonIgnore]
    internal string? AdapterText => ContentParts is { } parts
        ? JsonSerializer.Serialize(parts, typeof(JsonElement[]), JsonContext.Default)
        : Content;
    [JsonPropertyName("reasoning_content")]
    public object? ReasoningContent { get; set; }
    public string? Name { get; set; }
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
    [JsonPropertyName("tool_calls")]
    public VllmToolCall[]? ToolCalls { get; set; }  // function call时用
    [JsonPropertyName("tool_responses")]
    public VllmToolResponse[]? ToolResponses { get; set; }
    public IList<string>? Images { get; set; }
    [JsonIgnore]
    public JsonElement[]? AnthropicContentBlocks { get; set; }
}

[JsonConverter(typeof(VllmOpenAIMessageContentConverter))]
internal readonly record struct VllmOpenAIMessageContent(string Text, JsonElement[]? Parts = null);

/// <summary>
/// Writes request message content from its explicit representation: typed content parts become
/// a JSON array, everything else (user/assistant/system text and tool results) stays a JSON string.
/// Unlike <see cref="RawJsonStringConverter"/>, it never infers content parts from the text shape,
/// so text that happens to be a JSON array is sent verbatim as text.
/// </summary>
internal sealed class VllmOpenAIMessageContentConverter : JsonConverter<VllmOpenAIMessageContent>
{
    public override VllmOpenAIMessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return new(string.Empty, doc.RootElement.EnumerateArray().Select(part => part.Clone()).ToArray());
        }

        return new(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, VllmOpenAIMessageContent value, JsonSerializerOptions options)
    {
        if (value.Parts is { } parts)
        {
            writer.WriteStartArray();
            foreach (var part in parts)
                part.WriteTo(writer);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteStringValue(value.Text);
        }
    }
}



internal class VllmChatRequestMessage
{
    public required string Role { get; set; }
    public string? Content { get; set; }
    public IList<string>? Images { get; set; }
}

internal sealed class VllmOpenAITextContentPart
{
    public string Type { get; set; } = "text";
    public required string Text { get; set; }
}

internal sealed class VllmOpenAIImageContentPart
{
    public string Type { get; set; } = "image_url";
    [JsonPropertyName("image_url")]
    public required VllmOpenAIImageUrl ImageUrl { get; set; }
}

internal sealed class VllmOpenAIVideoContentPart
{
    public string Type { get; set; } = "video_url";
    [JsonPropertyName("video_url")]
    public required VllmOpenAIImageUrl VideoUrl { get; set; }
}

internal sealed class VllmOpenAIImageUrl
{
    public required string Url { get; set; }
}
