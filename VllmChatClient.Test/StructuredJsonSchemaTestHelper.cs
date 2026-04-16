using Microsoft.Extensions.AI;
using System.Text.Json;

namespace VllmChatClient.Test;

internal static class StructuredJsonSchemaTestHelper
{
    public static JsonElement CreateGreetingSchema()
        => JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "greeting": { "type": "string" }
              },
              "required": ["name", "greeting"],
              "additionalProperties": false
            }
            """).RootElement.Clone();

    public static List<ChatMessage> CreateGreetingMessages(string assistantName = "菲菲")
        =>
        [
            new ChatMessage(ChatRole.System, $"你是一个智能助手，名字叫{assistantName}"),
            new ChatMessage(ChatRole.User, $"请严格按指定 schema 返回 JSON 对象。输出必须且只能包含 name 和 greeting 两个字符串字段，其中 name 必须是“{assistantName}”，greeting 必须是一句问候语。不要输出代码块，也不要输出 JSON 之外的任何文字。")
        ];

    public static void AssertGreetingJson(string responseText, string assistantName = "菲菲")
    {
        var textContent = responseText.Trim();
        Assert.DoesNotContain("```", textContent);

        using var json = JsonDocument.Parse(textContent);
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);

        var propertyNames = json.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["greeting", "name"], propertyNames);
        Assert.Equal(assistantName, json.RootElement.GetProperty("name").GetString()?.Trim());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("greeting").GetString()));
    }
}
