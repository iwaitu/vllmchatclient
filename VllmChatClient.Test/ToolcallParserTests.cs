using Microsoft.Extensions.AI;
using System.Text.Json;

namespace VllmChatClient.Test;

public class ToolcallParserTests
{
    [Fact]
    public void TryParseBareToolCallsJson_ParsesArrayToolCallPayload()
    {
        const string payload = """[{"name":"Search","arguments":{"question":"南宁火车站在哪里"}}]""";

        var parsed = ToolcallParser.TryParseBareToolCallsJson(payload, out var calls);

        Assert.True(parsed);
        var call = Assert.Single(calls);
        Assert.Equal("Search", call.Name);

        using var arguments = JsonDocument.Parse(call.Arguments!);
        Assert.Equal("南宁火车站在哪里", arguments.RootElement.GetProperty("question").GetString());
    }

    [Fact]
    public void TryParseBareToolCallsJson_DoesNotParsePlainJsonResponse()
    {
        const string payload = """{"address":"南宁市青秀区方圆广场北面站前路1号","summary":"通过工具查询到的结果"}""";

        var parsed = ToolcallParser.TryParseBareToolCallsJson(payload, out var calls);

        Assert.False(parsed);
        Assert.Empty(calls);
    }

    [Fact]
    public void TryExtractBareToolCallsJson_ParsesToolCallInsideCodeFence()
    {
        var payload = """
            ```json
            [{"name":"Search","arguments":{"question":"南宁火车站在哪里"}}]
            ```
            """;

        var parsed = ToolcallParser.TryExtractBareToolCallsJson(ref payload, out var calls);

        Assert.True(parsed);
        var call = Assert.Single(calls);
        Assert.Equal("Search", call.Name);
        Assert.Equal(string.Empty, payload);
    }

    [Fact]
    public void TryParseBareToolCallsJson_ParsesParametersAlias()
    {
        const string payload = """{"name":"Search","parameters":{"question":"南宁火车站在哪里"}}""";

        var parsed = ToolcallParser.TryParseBareToolCallsJson(payload, out var calls);

        Assert.True(parsed);
        var call = Assert.Single(calls);
        Assert.Equal("Search", call.Name);

        using var parameters = JsonDocument.Parse(call.Arguments!);
        Assert.Equal("南宁火车站在哪里", parameters.RootElement.GetProperty("question").GetString());
    }

    [Fact]
    public void TryParseBareToolCallsJson_ParsesToolCallWrapper()
    {
        const string payload = """{"tool_call":[{"name":"CustomSearh","arguments":{"question":"南宁火车站在哪里"}}]}""";

        var parsed = ToolcallParser.TryParseBareToolCallsJson(payload, out var calls);

        Assert.True(parsed);
        var call = Assert.Single(calls);
        Assert.Equal("CustomSearh", call.Name);

        using var arguments = JsonDocument.Parse(call.Arguments!);
        Assert.Equal("南宁火车站在哪里", arguments.RootElement.GetProperty("question").GetString());
    }
}
