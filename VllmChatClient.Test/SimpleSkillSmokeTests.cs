using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

/// <summary>
/// 本地 smoke test：验证最简单的 skill 文件能被自动注入到请求中。
/// </summary>
public class SimpleSkillSmokeTests
{
    [Fact]
    public async Task ShouldInjectSingleSkillFromRuntimeSkillsFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"vllm-skill-smoke-{Guid.NewGuid():N}");
        var skillsDir = Path.Combine(tempRoot, "skills");
        Directory.CreateDirectory(skillsDir);
        await File.WriteAllTextAsync(Path.Combine(skillsDir, "simple.md"), "你必须用一句话回答。", Encoding.UTF8);

        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(tempRoot);

        try
        {
            var captureHandler = new RequestCaptureHandler();
            using var httpClient = new HttpClient(captureHandler);
            var client = new VllmQwen3NextChatClient("http://localhost:8000/{0}/{1}", modelId: "qwen3-next", httpClient: httpClient);

            var messages = new List<ChatMessage> { new(ChatRole.User, "介绍你自己") };
            var options = new VllmChatOptions { EnableSkills = true };

            _ = await client.GetResponseAsync(messages, options);

            Assert.NotNull(captureHandler.LastRequestJson);
            using var json = JsonDocument.Parse(captureHandler.LastRequestJson!);
            var requestMessages = json.RootElement.GetProperty("messages");

            Assert.Equal("system", requestMessages[0].GetProperty("role").GetString());
            var injectedSkillPrompt = requestMessages[0].GetProperty("content").GetString();
            Assert.Contains("Skill: simple", injectedSkillPrompt);
            Assert.Contains("你必须用一句话回答。", injectedSkillPrompt);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class RequestCaptureHandler : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
{
  "id": "chatcmpl-skill-smoke",
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
