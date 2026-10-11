
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
    [JsonIgnore]
    public string? Content { get; set; } = default!;
    [JsonPropertyName("content")]
    public VllmOpenAIMessageContent? SerializedContent
    {
        get => Content is null ? null : new(Content, Role == "tool");
        set => Content = value?.Text;
    }
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
internal readonly record struct VllmOpenAIMessageContent(string Text, bool IsToolResult);

internal sealed class VllmOpenAIMessageContentConverter : JsonConverter<VllmOpenAIMessageContent>
{
    private static readonly RawJsonStringConverter ContentPartsConverter = new();

    public override VllmOpenAIMessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty, IsToolResult: false);

    public override void Write(Utf8JsonWriter writer, VllmOpenAIMessageContent value, JsonSerializerOptions options)
    {
        // Function results are data. Even a JSON array resembling content parts
        // must remain text; only ordinary messages use the multimodal converter.
        if (value.IsToolResult)
            writer.WriteStringValue(value.Text);
        else
            ContentPartsConverter.Write(writer, value.Text, options);
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
