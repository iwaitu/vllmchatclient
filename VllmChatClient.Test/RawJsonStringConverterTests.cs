using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VllmChatClient.Test;

public class RawJsonStringConverterTests
{
    [Fact]
    public void JsonObjectString_IsSerializedAsString()
    {
        var payload = new ConverterProbe
        {
            Content = "{\"foo\":\"bar\"}"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal("{\"foo\":\"bar\"}", content.GetString());
    }

    [Fact]
    public void JsonArrayString_IsSerializedAsRawArray()
    {
        var payload = new ConverterProbe
        {
            Content = "[{\"type\":\"text\",\"text\":\"hello\"}]"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("hello", content[0].GetProperty("text").GetString());
    }

    private sealed class ConverterProbe
    {
        [JsonPropertyName("content")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string Content { get; set; } = string.Empty;
    }
}
