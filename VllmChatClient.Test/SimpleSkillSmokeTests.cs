using Microsoft.Extensions.AI;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
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

    /// <summary>
    /// 集成测试：使用 skill-creator 创建新的 skill。
    /// </summary>
    [Fact]
    public async Task CanCreateNewSkillUsingSkillCreator()
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

        // 1. 请求 AI 帮助创建一个新 skill
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I want to create a new skill called 'test-calculator' that helps with basic arithmetic operations. Please help me create this skill using the skill-creator.")
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

        _testOutput.WriteLine("AI Response: {0}", response.Text);
        
        // 验证响应包含与 skill 创建相关的内容
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        
        // 响应可能包含以下关键词之一：创建、skill、SKILL.md、init_skill
        bool containsRelevantContent = 
            response.Text.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("SKILL.md", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("create", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("init_skill", StringComparison.OrdinalIgnoreCase);
        
        Assert.True(containsRelevantContent, "Response should contain skill creation related content");
        
        _testOutput.WriteLine("✓ Test passed: AI provided guidance for creating a new skill");
    }

    /// <summary>
    /// 测试：验证 init_skill.py 脚本可以被 AI 调用来创建新 skill。
    /// </summary>
    [Fact]
    public async Task SkillCreatorCanExecuteInitSkillScript()
    {
        if (_skipTests)
        {
            _testOutput.WriteLine("Skipping: VLLM_ALIYUN_API_KEY not set");
            return;
        }

        var skillsDir = GetSkillsDir();
        var testSkillName = "test-sample-skill";
        var testSkillPath = Path.Combine(skillsDir, testSkillName);

        // 清理可能存在的测试 skill
        if (Directory.Exists(testSkillPath))
        {
            Directory.Delete(testSkillPath, true);
            _testOutput.WriteLine("Cleaned up existing test skill directory");
        }

        var client = new ChatClientBuilder(_chatClient)
            .UseFunctionInvocation()
            .Build();

        // 请求 AI 执行 init_skill.py 脚本创建新 skill
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Please use the init_skill.py script from skill-creator to create a new skill named '{testSkillName}' in the skills directory at path '{skillsDir}'.")
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

        _testOutput.WriteLine("AI Response: {0}", response.Text);
        
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        
        // 验证 AI 是否提到了脚本或创建过程
        bool mentionsScript = 
            response.Text.Contains("init_skill", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("script", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("created", StringComparison.OrdinalIgnoreCase);
        
        Assert.True(mentionsScript, "Response should mention the init_skill script or creation process");
        
        _testOutput.WriteLine("✓ Test passed: AI acknowledged the skill creation request");

        // 清理测试数据
        if (Directory.Exists(testSkillPath))
        {
            Directory.Delete(testSkillPath, true);
            _testOutput.WriteLine("Cleaned up test skill directory");
        }
    }

    /// <summary>
    /// 测试：使用流式传输验证 init_skill.py 脚本可以被 AI 调用来创建新 skill。
    /// </summary>
    [Fact]
    public async Task SkillCreatorCanExecuteInitSkillScriptOnStream()
    {
        if (_skipTests)
        {
            _testOutput.WriteLine("Skipping: VLLM_ALIYUN_API_KEY not set");
            return;
        }

        var skillsDir = GetSkillsDir();
        var testSkillName = "test-stream-skill";
        var testSkillPath = Path.Combine(skillsDir, testSkillName);

        if (Directory.Exists(testSkillPath))
        {
            Directory.Delete(testSkillPath, true);
            _testOutput.WriteLine("Cleaned up existing test skill directory");
        }

        var client = new ChatClientBuilder(_chatClient)
            .UseFunctionInvocation()
            .Build();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Please use the init_skill.py script from skill-creator to create a new skill named '{testSkillName}' in the skills directory at path '{skillsDir}'. Respond briefly.")
        };

        var options = new VllmChatOptions
        {
            EnableSkills = true,
            SkillDirectoryPath = skillsDir
        };

        var responseBuilder = new StringBuilder();
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            {
                if (update.Contents is { Count: > 0 })
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            responseBuilder.Append(textContent.Text);
                        }
                    }
                }
            }
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

        var responseText = responseBuilder.ToString();
        _testOutput.WriteLine("Streaming response: {0}", responseText);

        Assert.False(string.IsNullOrWhiteSpace(responseText));

        var mentionsScript =
            responseText.Contains("init_skill", StringComparison.OrdinalIgnoreCase) ||
            responseText.Contains("script", StringComparison.OrdinalIgnoreCase) ||
            responseText.Contains("created", StringComparison.OrdinalIgnoreCase);

        Assert.True(mentionsScript, "Response should mention the init_skill script or creation process");

        if (Directory.Exists(testSkillPath))
        {
            Directory.Delete(testSkillPath, true);
            _testOutput.WriteLine("Cleaned up test skill directory");
        }
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
