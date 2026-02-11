using Microsoft.Extensions.AI;

using System.Net;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace VllmChatClient.Test;

/// <summary>
/// Skill 集成测试：验证 skill 注入、多 skill 选择以及内置工具调用。
/// </summary>
public class SimpleSkillSmokeTests
{
    private readonly ITestOutputHelper _testOutput;
    private readonly IChatClient _chatClient;
    private readonly bool _skipTests;
    private const string MODEL = "MiniMax-M2.1";
    private readonly string? _cloud_apiKey;

    public SimpleSkillSmokeTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        _cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
        _skipTests = string.IsNullOrWhiteSpace(_cloud_apiKey);
        _chatClient = new VllmMiniMaxChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", _cloud_apiKey, MODEL);
    }

    private string GetSkillsDir() => Path.Combine(AppContext.BaseDirectory, "skills");

    private async Task LogSkillsDirectory(string skillsDir)
    {
        _testOutput.WriteLine("Skill directory: {0}", skillsDir);
        _testOutput.WriteLine("Skill directory exists: {0}", Directory.Exists(skillsDir));
        if (Directory.Exists(skillsDir))
        {
            foreach (var file in Directory.EnumerateFiles(skillsDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                _testOutput.WriteLine("  Skill file: {0}", Path.GetFileName(file));
                _testOutput.WriteLine("  Skill content: {0}", await File.ReadAllTextAsync(file));
            }

            foreach (var dir in Directory.EnumerateDirectories(skillsDir))
            {
                var skillMd = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillMd))
                {
                    _testOutput.WriteLine("  Skill file (subdir): {0}", Path.GetFileName(dir) + "/SKILL.md");
                    _testOutput.WriteLine("  Skill content: {0}", await File.ReadAllTextAsync(skillMd));
                }
            }
        }
    }

    /// <summary>
    /// 验证所有 skill 文件和内置工具定义都被注入到请求中。
    /// </summary>
    [Fact]
    public async Task AllSkillsAndBuiltInToolsInjected()
    {
        var skillsDir = GetSkillsDir();
        await LogSkillsDirectory(skillsDir);

        var handler = new FakeResponseHandler();
        using var httpClient = new HttpClient(handler);
        var localClient = new VllmMiniMaxChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", httpClient: httpClient, modelId: MODEL);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir
        };

        await localClient.GetResponseAsync(messages, options);

        Assert.NotNull(handler.LastRequestBody);
        _testOutput.WriteLine("Request payload: {0}", handler.LastRequestBody);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var requestMessages = doc.RootElement.GetProperty("messages");
        _testOutput.WriteLine("Message count: {0}", requestMessages.GetArrayLength());

        // system prompt injected
        Assert.True(requestMessages.GetArrayLength() >= 2);
        Assert.Equal("system", requestMessages[0].GetProperty("role").GetString());

        var systemText = requestMessages[0].GetProperty("content").GetString()!;
        _testOutput.WriteLine("System prompt:\n{0}", systemText);
        Assert.Contains("Skill: simple", systemText);
        Assert.Contains("Skill: coding", systemText);
        Assert.Contains("Skill: math", systemText);
        Assert.Contains("select the most relevant skill", systemText);
        Assert.Contains("ListSkillFiles", systemText);
        Assert.Contains("ReadSkillFile", systemText);

        // built-in tools injected
        var requestTools = doc.RootElement.GetProperty("tools");
        _testOutput.WriteLine("Tool count: {0}", requestTools.GetArrayLength());
        Assert.True(requestTools.GetArrayLength() >= 2, "Expected at least ListSkillFiles + ReadSkillFile");

        var toolNames = new List<string>();
        foreach (var tool in requestTools.EnumerateArray())
        {
            var name = tool.GetProperty("function").GetProperty("name").GetString()!;
            toolNames.Add(name);
        }
        _testOutput.WriteLine("Tools: {0}", string.Join(", ", toolNames));
        Assert.Contains("ListSkillFiles", toolNames);
        Assert.Contains("ReadSkillFile", toolNames);
    }

    /// <summary>
    /// 验证内置工具与用户自定义工具合并。
    /// </summary>
    [Fact]
    public async Task BuiltInToolsMergedWithUserTools()
    {
        var skillsDir = GetSkillsDir();

        var handler = new FakeResponseHandler();
        using var httpClient = new HttpClient(handler);
        var localClient = new VllmMiniMaxChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", _cloud_apiKey, modelId: MODEL, httpClient: httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir,
            Tools = [AIFunctionFactory.Create(GetWeather)]
        };

        await localClient.GetResponseAsync(messages, options);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var requestTools = doc.RootElement.GetProperty("tools");
        var toolNames = new List<string>();
        foreach (var tool in requestTools.EnumerateArray())
        {
            toolNames.Add(tool.GetProperty("function").GetProperty("name").GetString()!);
        }
        _testOutput.WriteLine("Tools: {0}", string.Join(", ", toolNames));

        Assert.Contains("ListSkillFiles", toolNames);
        Assert.Contains("ReadSkillFile", toolNames);
        Assert.Contains("GetWeather", toolNames);
        Assert.Equal(3, toolNames.Count);
    }

    /// <summary>
    /// 验证 EnableSkills=false 且 SkillDirectoryPath 为空时，不注入 skill 和内置工具。
    /// </summary>
    [Fact]
    public async Task SkillsNotInjectedWhenDisabled()
    {
        var handler = new FakeResponseHandler();
        using var httpClient = new HttpClient(handler);
        var localClient = new VllmMiniMaxChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", _cloud_apiKey, MODEL, httpClient: httpClient);
        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var options = new VllmChatOptions
        {
            EnableSkills = false,
            SkillDirectoryPath = null
        };

        await localClient.GetResponseAsync(messages, options);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var requestMessages = doc.RootElement.GetProperty("messages");
        Assert.Equal("user", requestMessages[0].GetProperty("role").GetString());

        // no tools injected
        Assert.False(doc.RootElement.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0,
            "Expected no tools when skills are disabled");
    }

    /// <summary>
    /// 集成测试：数学问题应触发 math skill（回答中包含 "Answer:"）。
    /// </summary>
    [Fact]
    public async Task MathSkillSelectedForMathQuestion()
    {
        if (_skipTests)
        {
            _testOutput.WriteLine("Skipping: VLLM_ALIYUN_API_KEY not set");
            return;
        }

        var skillsDir = GetSkillsDir();
        await LogSkillsDirectory(skillsDir);

        var client = new ChatClientBuilder(_chatClient)
            .UseFunctionInvocation()
            .Build();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "12 * 15 + 8 = ?")
        };
        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir
        };

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages, options);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient_quota") || ex.Message.Contains("rate_limit"))
        {
            _testOutput.WriteLine("Skipping: API quota/rate limit exceeded - {0}", ex.Message);
            return;
        }
        catch (HttpRequestException ex)
        {
            _testOutput.WriteLine("Skipping: API unreachable - {0}", ex.Message);
            return;
        }

        _testOutput.WriteLine("Response: {0}", response.Text);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.Contains("Answer:", response.Text);
    }

    /// <summary>
    /// 集成测试：无需手动添加工具，内置工具自动可用。
    /// </summary>
    [Fact]
    public async Task BuiltInToolsInvokedByModel()
    {
        if (_skipTests)
        {
            _testOutput.WriteLine("Skipping: VLLM_ALIYUN_API_KEY not set");
            return;
        }

        var skillsDir = GetSkillsDir();
        await LogSkillsDirectory(skillsDir);

        var client = new ChatClientBuilder(_chatClient)
            .UseFunctionInvocation()
            .Build();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Please list available skill files and read the math skill, then tell me its rules.")
        };
        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir
        };

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages, options);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient_quota") || ex.Message.Contains("rate_limit"))
        {
            _testOutput.WriteLine("Skipping: API quota/rate limit exceeded - {0}", ex.Message);
            return;
        }
        catch (HttpRequestException ex)
        {
            _testOutput.WriteLine("Skipping: API unreachable - {0}", ex.Message);
            return;
        }

        _testOutput.WriteLine("Response: {0}", response.Text);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.Contains("Answer:", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [System.ComponentModel.Description("Gets the current weather")]
    private static string GetWeather() => "It's sunny, 25°C";

    /// <summary>
    /// 验证 skill-creator 子目录中的 SKILL.md 能被正确加载。
    /// </summary>
    [Fact]
    public async Task SkillCreatorLoadedVerify()
    {
        var skillsDir = GetSkillsDir();
        var handler = new FakeResponseHandler();
        using var httpClient = new HttpClient(handler);
        var localClient = new VllmMiniMaxChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", _cloud_apiKey, MODEL, httpClient: httpClient);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir
        };

        await localClient.GetResponseAsync(messages, options);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var requestMessages = doc.RootElement.GetProperty("messages");
        var systemText = requestMessages[0].GetProperty("content").GetString()!;
        
        _testOutput.WriteLine("System prompt snippet: {0}", systemText.Length > 100 ? systemText.Substring(0, 100) + "..." : systemText);

        Assert.Contains("Skill: skill-creator", systemText);
    }

    private sealed class FakeResponseHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id": "chatcmpl-fake",
                      "object": "chat.completion",
                      "created": 1710000000,
                      "model": "kimi-k2.5",
                      "choices": [
                        {
                          "index": 0,
                          "message": { "role": "assistant", "content": "ok" },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
                    }
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}
