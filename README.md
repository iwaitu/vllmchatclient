# vllmchatclient

[![GitHub stars](https://img.shields.io/github/stars/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/network)
[![GitHub issues](https://img.shields.io/github/issues/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/issues)
[![GitHub license](https://img.shields.io/github/license/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/blob/master/LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/commits/main)
[![.NET](https://img.shields.io/badge/platform-.NET%208-blueviolet)](https://dotnet.microsoft.com/)

# C# vLLM Chat Client

A comprehensive .NET 8 chat client library that supports various LLM models including **GPT-OSS-120B**, **Qwen3**, **Qwen3-Next**, **QwQ-32B**, **Gemma3**, **DeepSeek-R1**, **Kimi K2**, **GLM 4.6**, **Gemini 3** with advanced reasoning capabilities.

## 🚀 Features

- ✅ **Multi-model Support**: Qwen3, QwQ, Gemma3, DeepSeek-R1, GLM-4 / 4.6, GPT-OSS-120B/20B, Qwen3-Next, Kimi K2, Gemini 3
- ✅ **Reasoning Chain Support**: Built-in thinking/reasoning capabilities for supported models
- ✅ **Stream Function Calls**: Real-time function calling with streaming responses
- ✅ **Multiple Deployment Options**: Local vLLM deployment and cloud API support
- ✅ **Performance Optimized**: Efficient streaming and memory management
- ✅ **.NET 8 Ready**: Full compatibility with the latest .NET platform

## 📦 Project Repository

**GitHub**: https://github.com/iwaitu/vllmchatclient

---

## 本次更新

- 新增 GLM 4.6/4.7 思维链支持：`VllmGlm46ChatClient`，支持推理分段流式输出（思考/答案）与函数调用。
- 新增 `GlmChatOptions`：通过 `ThinkingEnabled` 开关控制是否在请求体中发送智普官方平台所需的 `thinking: { type: "enabled" }`（默认关闭）。
- 在“支持的客户端”表新增 `VllmGlm46ChatClient` 条目。
- 更新 GLM 4.6/4.7 使用示例（见下文“GLM 4.6/4.7 Thinking Example”），并说明智普官方平台思维链参数支持。
- 强化 Qwen3-Next 能力：新增“串行/并行函数调用”示例、手动工具编排的流式调用示例、以及严格的 JSON 纯文本输出（无 codeblock）示例。
- 说明 `VllmQwen3NextChatClient` 支持多个模型：通过构造函数 `modelId` 或 `ChatOptions.ModelId` 切换（thinking / instruct 等）。
- 新增标签提取示例（基于 JSON 解析与正则匹配）。
- 新增 Gemini 3 支持（`VllmGemini3ChatClient`）：
  - 文本与流式响应、推理级别 Normal/Low
  - 工具调用（单个/并行/自动执行/流式）完整测试通过
  - 新增调试测试：`Gemini3Test`、`GeminiDebugTest`（含多轮 thoughtSignature 调试）
  - 新增文档：`docs/Gemini3ReasoningExplanation.md`、`docs/Gemini3FunctionCallSupport.md`、`docs/Gemini3DebugTestGuide.md`、`docs/Gemini3FunctionCallDebugGuide.md`、`docs/Gemini3FunctionCallTestResults.md`
  - 说明：基于当前测试，函数调用无需显式回传 thoughtSignature，仍可正常完成多轮调用（详见文档）

---

## 🔥 Latest Updates

### 🆕 GLM 4.6 Thinking Model Support
- **VllmGlm46ChatClient** added with full reasoning (thinking) stream separation.
- Supports `glm-4.6` and `glm-4.7`.
- Compatible with existing tool/function invocation pipeline.
- Supports Zhipu official platform thinking parameter via `GlmChatOptions.ThinkingEnabled`.

### 🆕 New GPT-OSS-20B/120B Support
- **VllmGptOssChatClient** - Support for OpenAI's GPT-OSS-120B model with full reasoning capabilities
- Advanced reasoning chain processing with `ReasoningChatResponseUpdate`
- Compatible with OpenRouter and other GPT-OSS providers
- Enhanced debugging and performance optimizations

### 🆕 GLM-4 Support
- **VllmGlmZ1ChatClient** - Support for GLM-4 models with reasoning capabilities
- **VllmGlm4ChatClient** - Standard GLM-4 chat functionality

### 🆕 Enhanced Qwen 2507 Models
- **VllmQwen2507ChatClient** - For qwen3-235b-a22b-instruct-2507 (standard)
- **VllmQwen2507ReasoningChatClient** - For qwen3-235b-a22b-thinking-2507 (with reasoning)

### 🆕 Qwen3-Next 80B (Thinking vs Instruct)
- **VllmQwen3NextChatClient** added.
- Supports both `qwen3-next-80b-a3b-thinking` (reasoning output, exposes `ReasoningChatResponse` / streaming `ReasoningChatResponseUpdate`) and `qwen3-next-80b-a3b-instruct` (standard instruct style output without reasoning chain).
- Unified API: switch model by passing the desired modelId in constructor or per-request via `ChatOptions.ModelId`.
- New examples: Serial/Parallel tool calls, manual tool orchestration in streaming, JSON-only output formatting.

### 🆕 Kimi K2 Support
- **VllmKimiK2ChatClient** added.
- Supports `kimi-k2-thinking` (reasoning output) and future instruct variants.
- Seamless reasoning streaming via `ReasoningChatResponseUpdate` (thinking vs final answer segments).
- Full function invocation support (automatic or manual tool call handling).

### 🆕 Gemini 3 Support & Tool Calling
- **VllmGemini3ChatClient** added (Google Gemini API)。
- Features: text & streaming, ReasoningLevel (Normal/Low), full tool calling (single / parallel / automatic / streaming)。
- Tests: `Gemini3Test` 全部通过（含多轮与并行工具调用）、`GeminiDebugTest` 覆盖原生 API 思维签名与多轮函数调用调试。
- Docs: 详见 `docs/Gemini3*` 文档合集。

---

## 🏗️ Supported Clients

| Client | Deployment | Model Support | Reasoning | Function Calls |
|--------|------------|---------------|-----------|----------------|
| `VllmGptOssChatClient` | OpenRouter/Cloud | GPT-OSS-120B/20B | ✅ Full | ✅ Stream |
| `VllmQwen3ChatClient` | Local vLLM | Qwen3-32B/235B | ✅ Toggle | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | Multiple modelIds (e.g. qwen3-next-80b-a3b-thinking / qwen3-next-80b-a3b-instruct) | ✅ (thinking model) | ✅ Stream |
| `VllmQwqChatClient` | Local vLLM | QwQ-32B | ✅ Full | ✅ Stream |
| `VllmGemmaChatClient` | Local vLLM | Gemma3-27B | ❌ | ✅ Stream |
| `VllmGemini3ChatClient` | Cloud API (Google Gemini) | gemini-3-pro-preview | Signature (hidden) | ✅ Stream |
| `VllmDeepseekR1ChatClient` | Cloud API | DeepSeek-R1 | ✅ Full | ❌ |
| `VllmGlmZ1ChatClient` | Local vLLM | GLM-4 | ✅ Full | ✅ Stream |
| `VllmGlm4ChatClient` | Local vLLM | GLM-4 | ❌ | ✅ Stream |
| `VllmGlm46ChatClient` | Cloud API (Zhipu official) / OpenAI compatible | glm-4.6 / glm-4.7 | ✅ Full (via `GlmChatOptions`) | ✅ Stream |
| `VllmQwen2507ChatClient` | Cloud API | qwen3-235b-a22b-instruct-2507 | ❌ | ✅ Stream |
| `VllmQwen2507ReasoningChatClient` | Cloud API | qwen3-235b-a22b-thinking-2507 | ✅ Full | ✅ Stream |
| `VllmKimiK2ChatClient` | Cloud API (DashScope) | kimi-k2-(thinking/instruct) | ✅ (thinking model) | ✅ Stream |

> 注：Gemini 3 的推理采用加密的 thought signature，不输出可读推理文本；函数调用在当前测试中无需显式回传签名亦可完成多轮调用。

---

## 🐳 Docker Deployment Examples

### Qwen3 vLLM Deployment:
```bash
docker run -it --gpus all -p 8000:8000 \
  -v /models/Qwen3-32B-FP8:/models/Qwen3-32B-FP8 \
  --restart always \
  -e VLLM_USE_V1=1 \
  vllm/llm-openai:v0.8.5 \
  --model /models/Qwen3-32B-FP8 \
  --enable-auto-tool-choice \
  --tool-call-parser hermes \
  --trust-remote-code \
  --max-model-len 131072 \
  --tensor-parallel-size 2 \
  --gpu_memory_utilization 0.8 \
  --served-model-name "qwen3"
```

### QwQ vLLM Deployment:
```bash
docker run -it --gpus all -p 8000:8000 \
  -v /models/Qwen3-32B-FP8:/models/Qwen3-32B-FP8 \
  --restart always \
  -e VLLM_USE_V1=1 \
  vllm/llm-openai:v0.8.5 \
  --model /models/Qwen3-32B-FP8 \
  --enable-auto-tool-choice \
  --tool-call-parser llama3_json \
  --trust-remote-code \
  --max-model-len 131072 \
  --tensor-parallel-size 2 \
  --gpu_memory_utilization 0.8 \
  --served-model-name "qwen3"
```

### Gemma3 vLLM Deployment:
```bash
docker run -it --gpus all -p 8000:8000 \
  -v /models/gemma-3-27b-it-FP8-Dynamic:/models/gemma-3-27b-it-FP8-Dynamic \
  -v /home/lc/work/gemma3.jinja:/home/lc/work/gemma3.jinja \
  -e TZ=Asia/Shanghai \
  -e VLLM_USE_V1=1 \
  --restart always \
  vllm/llm-openai:v0.8.2 \
  --model /models/gemma-3-27b-it-FP8-Dynamic \
  --enable-auto-tool-choice \
  --tool-call-parser pythonic \
  --chat-template /home/lc/work/gemma3.jinja \
  --trust-remote-code \
  --max-model-len 128000 \
  --tensor-parallel-size 2 \
  --gpu_memory_utilization 0.8 \
  --served-model-name "gemma3"
```

---

## 💻 Usage Examples

### 🆕 GLM 4.6/4.7 Thinking Example

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Glm4;

IChatClient glm46 = new VllmGlm46ChatClient(
    "http://localhost:8000/{0}/{1}", // or your OpenAI-compatible endpoint
    null,
    "glm-4.6");

// Enable Zhipu official platform thinking chain parameter:
// thinking: { "type": "enabled" }
var opts = new GlmChatOptions { ThinkingEnabled = true };

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new(ChatRole.User, "解释一下快速排序的思想并举一个简单例子。")
};

string reasoning = string.Empty;
string answer = string.Empty;
await foreach (var update in glm46.GetStreamingResponseAsync(messages, opts))
{
    if (update is ReasoningChatResponseUpdate r)
    {
        if (r.Thinking)
            reasoning += r.Text; // reasoning phase
        else
            answer += r.Text;    // final answer phase
    }
    else
    {
        answer += update.Text;
    }
}
Console.WriteLine($"Reasoning: {reasoning}\nAnswer: {answer}");
```

### 🆕 GPT-OSS-120B with Reasoning (OpenRouter)

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;

[Description("Gets weather information")]
static string GetWeather(string city) => $"Weather in {city}: Sunny, 25°C";

// Initialize GPT-OSS client
IChatClient gptOssClient = new VllmGptOssChatClient(
    "https://openrouter.ai/api/v1", 
    "your-api-token", 
    "openai/gpt-oss-120b");

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "You are a helpful assistant with reasoning capabilities."),
    new ChatMessage(ChatRole.User, "What's the weather like in Tokyo? Please think through this step by step.")
};

var chatOptions = new ChatOptions
{
    Temperature = 0.7f,
    ReasoningLevel = GptOssReasoningLevel.Medium,    // Set reasoning level,controls depth of reasoning
    Tools = [AIFunctionFactory.Create(GetWeather)]
};

// Stream response with reasoning
string reasoning = string.Empty;
string answer = string.Empty;

await foreach (var update in gptOssClient.GetStreamingResponseAsync(messages, chatOptions))
{
    if (update is ReasoningChatResponseUpdate reasoningUpdate)
    {
        if (reasoningUpdate.Thinking)
        {
            // Capture the model's reasoning process
            reasoning += reasoningUpdate.Reasoning;
            Console.WriteLine($"🧠 Thinking: {reasoningUpdate.Reasoning}");
        }
        else
        {
            // Capture the final answer
            answer += reasoningUpdate.Text;
            Console.WriteLine($"💬 Response: {reasoningUpdate.Text}");
        }
    }
}

Console.WriteLine($"\n📝 Full Reasoning: {reasoning}");
Console.WriteLine($"✅ Final Answer: {answer}");
```

### 🆕 Qwen3-Next 80B (Thinking vs Instruct)

```csharp
using Microsoft.Extensions.AI;

// Choose model: reasoning variant or instruct variant
var apiKey = "your-dashscope-api-key";
// Reasoning (with thinking chain)
IChatClient thinkingClient = new VllmQwen3NextChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}",
    apiKey,
    "qwen3-next-80b-a3b-thinking");

// Instruct (no reasoning chain)
IChatClient instructClient = new VllmQwen3NextChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}",
    apiKey,
    "qwen3-next-80b-a3b-instruct");

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new(ChatRole.User,   "简单介绍下量子计算。")
};

// Reasoning streaming example
await foreach (var update in thinkingClient.GetStreamingResponseAsync(messages))
{
    if (update is ReasoningChatResponseUpdate r)
    {
        if (r.Thinking)
            Console.Write(r.Text);   // reasoning / thinking phase
        else
            Console.Write(r.Text);   // final answer phase
    }
    else
    {
        Console.Write(update.Text);
    }
}

// Instruct (single response)
var resp = await instructClient.GetResponseAsync(messages);
Console.WriteLine(resp.Text);
```

### 🆕 Qwen3-Next Advanced Function Calls (Serial / Parallel / Manual Streaming)

```csharp
using Microsoft.Extensions.AI;

[Description("获取南宁的天气情况")]
static string GetWeather() => "现在正在下雨。";

[Description("Searh")]
static string Search([Description("需要搜索的问题")] string question) => "南宁市青秀区方圆广场北面站前路1号。";

IChatClient baseClient = new VllmQwen3NextChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}",
    Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY"),
    "qwen3-next-80b-a3b-thinking");

IChatClient client = new ChatClientBuilder(baseClient)
    .UseFunctionInvocation()
    .Build();

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
    new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
};

ChatOptions opts = new()
{
    Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
};

// Parallel tool calls example (also supports serial depending on prompt)
await foreach (var update in client.GetStreamingResponseAsync(messages, opts))
{
    if (update is ReasoningChatResponseUpdate r)
    {
        Console.Write(r.Text);
    }
    else
    {
        Console.Write(update.Text);
    }
}

// Manual streaming tool orchestration
messages = new()
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
};
string answer = string.Empty;
await foreach (var update in client.GetStreamingResponseAsync(messages, opts))
{
    if (update.FinishReason == ChatFinishReason.ToolCalls)
    {
        foreach (var fc in update.Contents.OfType<FunctionCallContent>())
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));
            if (fc.Name == "GetWeather")
            {
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(fc.CallId, GetWeather())]));
            }
            else if (fc.Name == "Search")
            {
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(fc.CallId, Search("南宁火车站"))]));
            }
        }
    }
    else
    {
        answer += update.Text;
    }
}
Console.WriteLine(answer);
```

### 🆕 JSON-only Output (No Code Block)

```csharp
using Microsoft.Extensions.AI;

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new(ChatRole.User, "请输出json格式的问候语，不要使用 codeblock。")
};
var options = new ChatOptions { MaxOutputTokens = 100 };
var resp = await baseClient.GetResponseAsync(messages, options);
var text = resp.Text; // Ensure no ``` code blocks and extract JSON via regex if needed
```

### Qwen3 with Reasoning Toggle

```csharp
using Microsoft.Extensions.AI;

[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.1 ? "It's sunny" : "It's raining";

IChatClient vllmclient = new VllmQwen3ChatClient("http://localhost:8000/{0}/{1}", null, "qwen3");
IChatClient client2 = new ChatClientBuilder(vllmclient)
    .UseFunctionInvocation()
    .Build();

var messages2 = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User, "今天天气如何？")
};

Qwen3ChatOptions chatOptions = new()
{
    Tools = [AIFunctionFactory.Create(GetWeather)],
    NoThinking = true  // Toggle reasoning on/off
};

string res = string.Empty;
await foreach (var update in client2.GetStreamingResponseAsync(messages2, chatOptions))
{
    res += update.Text;
}
```

### QwQ with Full Reasoning Support

```csharp
using Microsoft.Extensions.AI;

[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

IChatClient vllmclient2 = new VllmQwqChatClient("http://localhost:8000/{0}/{1}", null, "qwq");

var messages3 = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User, "今天天气如何？")
};

ChatOptions chatOptions2 = new()
{
    Tools = [AIFunctionFactory.Create(GetWeather)]
};

// Stream with reasoning separation
private async Task<(string answer, string reasoning)> StreamChatResponseAsync(
    List<ChatMessage> messages, ChatOptions chatOptions)
{
    string answer = string.Empty;
    string reasoning = string.Empty;
    
    await foreach (var update in vllmclient2.GetStreamingResponseAsync(messages, chatOptions))
    {
        if (update is ReasoningChatResponseUpdate reasoningUpdate)
        {
            if (!reasoningUpdate.Thinking)
            {
                answer += reasoningUpdate.Text;
            }
            else
            {
                reasoning += reasoningUpdate.Text;
            }
        }
        else
        {
            answer += update.Text;
        }
    }
    return (answer, reasoning);
}

var (answer3, reasoning3) = await StreamChatResponseAsync(messages3, chatOptions2);
```

### DeepSeek-R1 with Reasoning

```csharp
using Microsoft.Extensions.AI;

IChatClient client3 = new VllmDeepseekR1ChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", 
    "your-api-key", 
    "deepseek-r1");

var messages4 = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User, "你是谁？")
};

string res4 = string.Empty;
string think = string.Empty;

await foreach (ReasoningChatResponseUpdate update in client3.GetStreamingResponseAsync(messages4))
{
    if (update.Thinking)
    {
        think += update.Text;
    }
    else
    {
        res4 += update.Text;
    }
}
```

---

## 🔧 Advanced Features

### Reasoning Chain Processing
All reasoning-capable clients support the `ReasoningChatResponseUpdate` interface:

```csharp
await foreach (var update in client.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate reasoningUpdate)
    {
        if (reasoningUpdate.Thinking)
        {
            // Process thinking/reasoning content
            Console.WriteLine($"🤔 Reasoning: {reasoningUpdate.Reasoning}");
        }
        else
        {
            // Process final response
            Console.WriteLine($"💬 Answer: {reasoningUpdate.Text}");
        }
    }
}
```

### Function Calling with Streaming
All clients support real-time function calling:

```csharp
[Description("Search for location information")]
static string Search([Description("Search query")] string query)
{
    return "Location found: Beijing, China";
}

ChatOptions options2 = new()
{
    Tools = [AIFunctionFactory.Create(Search)],
    Temperature = 0.7f
};

await foreach (var update in client.GetStreamingResponseAsync(messages, options2))
{
    // Handle function calls and responses in real-time
    foreach (var content in update.Contents)
    {
        if (content is FunctionCallContent functionCall)
        {
            Console.WriteLine($"🔧 Calling: {functionCall.Name}");
        }
    }
}
```

---

## 🏆 Performance & Optimizations

- **Stream Processing**: Efficient real-time response handling
- **Memory Management**: Optimized for long conversations
- **Error Handling**: Robust error recovery and debugging support
- **JSON Parsing**: High-performance serialization with System.Text.Json
- **Connection Pooling**: Shared HttpClient for optimal resource usage

---

## 📋 Requirements

- **.NET 8.0** or higher
- **Microsoft.Extensions.AI** framework
- **Newtonsoft.Json** for JSON processing
- **System.Text.Json** for high-performance scenarios

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests。

---

## 📄 License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
