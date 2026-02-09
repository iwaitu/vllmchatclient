using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class SkillLoadingTests
{
    [Fact]
    public async Task GetResponseAsync_LoadsSkillsFromDirectory_WhenEnabled()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"vllm-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var skillsDir = Path.Combine(workDir, "skills");
        Directory.CreateDirectory(skillsDir);
        await File.WriteAllTextAsync(Path.Combine(skillsDir, "coding.md"), "always write concise output");

        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workDir);
        try
        {
            var handler = new CaptureHandler();
            using var httpClient = new HttpClient(handler);
            var client = new VllmQwen3NextChatClient("http://localhost:8000/{0}/{1}", modelId: "qwen3-next", httpClient: httpClient);

            var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
            var options = new VllmChatOptions { EnableSkills = true };

            var response = await client.GetResponseAsync(messages, options);

            Assert.NotNull(response);
            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var requestMessages = doc.RootElement.GetProperty("messages");

            Assert.Equal("system", requestMessages[0].GetProperty("role").GetString());
            var systemText = requestMessages[0].GetProperty("content").GetString();
            Assert.Contains("Skill: coding", systemText);
            Assert.Contains("always write concise output", systemText);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetResponseAsync_DoesNotLoadSkills_WhenDisabled()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"vllm-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var skillsDir = Path.Combine(workDir, "skills");
        Directory.CreateDirectory(skillsDir);
        await File.WriteAllTextAsync(Path.Combine(skillsDir, "coding.md"), "always write concise output");

        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workDir);
        try
        {
            var handler = new CaptureHandler();
            using var httpClient = new HttpClient(handler);
            var client = new VllmQwen3NextChatClient("http://localhost:8000/{0}/{1}", modelId: "qwen3-next", httpClient: httpClient);

            var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
            var options = new VllmChatOptions { EnableSkills = false };

            _ = await client.GetResponseAsync(messages, options);

            Assert.NotNull(handler.LastRequestBody);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var requestMessages = doc.RootElement.GetProperty("messages");
            Assert.Equal("user", requestMessages[0].GetProperty("role").GetString());
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(workDir, recursive: true);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
{
  "id": "chatcmpl-test",
  "object": "chat.completion",
  "created": 1710000000,
  "model": "qwen3-next",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "ok"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 1,
    "completion_tokens": 1,
    "total_tokens": 2
  }
}
""", Encoding.UTF8, "application/json")
            };
        }
    }
}
