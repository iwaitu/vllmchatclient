
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
            // 判断 value 是否可能是 JSON 对象或数组
            var trimmed = value.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(value))
                    {
                        doc.WriteTo(writer);
                        return;
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
    [JsonConverter(typeof(RawJsonStringConverter))]
    public string? Content { get; set; } = default!;
    public string? Name { get; set; }
    public string? ToolCallId { get; set; }
    public VllmToolCall[]? ToolCalls { get; set; }  // function call时用
    public IList<string>? Images { get; set; }
}



internal class VllmChatRequestMessage
{
    public required string Role { get; set; }
    public string? Content { get; set; }
    public IList<string>? Images { get; set; }
}

