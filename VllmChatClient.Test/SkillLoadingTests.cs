using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public sealed class SkillLoadingTests : IDisposable
{
    private const string Model = "test-model";
    private readonly string _skillsDir;

    public SkillLoadingTests()
    {
        _skillsDir = Path.Combine(Path.GetTempPath(), $"vllm-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_skillsDir);

        File.WriteAllText(
            Path.Combine(_skillsDir, "math.md"),
            "Always show the formula first.\nKeep the hidden reasoning out of the initial prompt.");

        var weatherDir = Path.Combine(_skillsDir, "weather");
        Directory.CreateDirectory(weatherDir);
        File.WriteAllText(
            Path.Combine(weatherDir, "SKILL.md"),
            """
            ---
            name: weather-guide
            description: Use this skill when users ask for weather briefings.
            ---

            # Weather Guide

            SECRET BODY TEXT
            Follow the weather escalation workflow.
            Never expose this text in metadata.
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_skillsDir))
        {
            Directory.Delete(_skillsDir, recursive: true);
        }
    }

    [Fact]
    public async Task RequestInjectsOnlySkillMetadata()
    {
        using var handler = new SequenceResponseHandler(CreateTextResponse("ok"));
        using var httpClient = new HttpClient(handler);
        using var client = new VllmMiniMaxChatClient("http://localhost:8000/v1/{1}", httpClient: httpClient, modelId: Model);

        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = _skillsDir,
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        Assert.Single(handler.RequestBodies);
        using var requestDoc = JsonDocument.Parse(handler.RequestBodies[0]);
        var messages = requestDoc.RootElement.GetProperty("messages");
        var systemText = messages[0].GetProperty("content").GetString();

        Assert.NotNull(systemText);
        Assert.Contains("Only the skill metadata below is loaded into context right now.", systemText);
        Assert.Contains("Skill: math", systemText);
        Assert.Contains("Description: Always show the formula first.", systemText);
        Assert.Contains("Skill: weather-guide", systemText);
        Assert.Contains("Description: Use this skill when users ask for weather briefings.", systemText);
        Assert.Contains("File: weather/SKILL.md", systemText);
        Assert.DoesNotContain("SECRET BODY TEXT", systemText);
        Assert.DoesNotContain("Never expose this text in metadata.", systemText);

        var toolNames = requestDoc.RootElement
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("function").GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("ListSkillFiles", toolNames);
        Assert.Contains("ReadSkillFile", toolNames);
        Assert.Contains("CreateSkillFile", toolNames);
    }

    [Fact]
    public async Task ReadSkillFileLoadsFullSkillBodyOnDemand()
    {
        using var handler = new SequenceResponseHandler(
            CreateToolCallResponse("ReadSkillFile", "{\"fileName\":\"weather-guide\"}"),
            CreateTextResponse("done"));
        using var httpClient = new HttpClient(handler);
        using var innerClient = new VllmMiniMaxChatClient("http://localhost:8000/v1/{1}", httpClient: httpClient, modelId: Model);
        var client = new ChatClientBuilder(innerClient)
            .UseFunctionInvocation()
            .Build();

        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = _skillsDir,
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Give me the weather workflow")], options);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.DoesNotContain("SECRET BODY TEXT", handler.RequestBodies[0]);

        Assert.Contains("SECRET BODY TEXT", handler.RequestBodies[1]);
        Assert.Contains("Follow the weather escalation workflow.", handler.RequestBodies[1]);
        Assert.Contains("Never expose this text in metadata.", handler.RequestBodies[1]);
    }

    private static string CreateTextResponse(string content) =>
        $$"""
        {
          "id": "chatcmpl-final",
          "object": "chat.completion",
          "created": 1710000000,
          "model": "{{Model}}",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "{{content}}" },
              "finish_reason": "stop"
            }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
        }
        """;

    private static string CreateToolCallResponse(string toolName, string argumentsJson) =>
        $$"""
        {
          "id": "chatcmpl-tool",
          "object": "chat.completion",
          "created": 1710000000,
          "model": "{{Model}}",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  {
                    "id": "call_read_skill",
                    "type": "function",
                    "function": {
                      "name": "{{toolName}}",
                      "arguments": {{JsonSerializer.Serialize(argumentsJson)}}
                    }
                  }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
        }
        """;

    private sealed class SequenceResponseHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly IReadOnlyList<string> _responses = responses;
        private int _index;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (_index >= _responses.Count)
            {
                throw new InvalidOperationException("No more fake responses were configured.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses[_index++], Encoding.UTF8, "application/json")
            };
        }
    }
}
