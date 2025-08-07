# vllmchatclient

[![GitHub stars](https://img.shields.io/github/stars/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/network)
[![GitHub issues](https://img.shields.io/github/issues/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/issues)
[![GitHub license](https://img.shields.io/github/license/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/blob/master/LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/commits/main)
[![.NET](https://img.shields.io/badge/platform-.NET%208-blueviolet)](https://dotnet.microsoft.com/)

# C# vLLM Chat Client

A comprehensive .NET 8 chat client library that supports various LLM models including **GPT-OSS-120B**, **Qwen3**, **QwQ-32B**, **Gemma3**, and **DeepSeek-R1** with advanced reasoning capabilities.

## 🚀 Features

- ✅ **Multi-model Support**: Qwen3, QwQ, Gemma3, DeepSeek-R1, GLM-4, GPT-OSS-120B/20B
- ✅ **Reasoning Chain Support**: Built-in thinking/reasoning capabilities for supported models
- ✅ **Stream Function Calls**: Real-time function calling with streaming responses
- ✅ **Multiple Deployment Options**: Local vLLM deployment and cloud API support
- ✅ **Performance Optimized**: Efficient streaming and memory management
- ✅ **.NET 8 Ready**: Full compatibility with the latest .NET platform

## 📦 Project Repository

**GitHub**: https://github.com/iwaitu/vllmchatclient

---

## 🔥 Latest Updates

### 🆕 New GPT-OSS-120B Support
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

---

## 🏗️ Supported Clients

| Client | Deployment | Model Support | Reasoning | Function Calls |
|--------|------------|---------------|-----------|----------------|
| `VllmGptOssChatClient` | OpenRouter/Cloud | GPT-OSS-120B/20B | ✅ Full | ✅ Stream |
| `VllmQwen3ChatClient` | Local vLLM | Qwen3-32B/235B | ✅ Toggle | ✅ Stream |
| `VllmQwqChatClient` | Local vLLM | QwQ-32B | ✅ Full | ✅ Stream |
| `VllmGemmaChatClient` | Local vLLM | Gemma3-27B | ❌ | ✅ Stream |
| `VllmDeepseekR1ChatClient` | Cloud API | DeepSeek-R1 | ✅ Full | ✅ Stream |
| `VllmGlmZ1ChatClient` | Local vLLM | GLM-4 | ✅ Full | ✅ Stream |
| `VllmGlm4ChatClient` | Local vLLM | GLM-4 | ❌ | ✅ Stream |
| `VllmQwen2507ChatClient` | Cloud API | Qwen3-235B-2507 | ❌ | ✅ Stream |
| `VllmQwen2507ReasoningChatClient` | Cloud API | Qwen3-235B-2507 | ✅ Full | ✅ Stream |

---

## 🐳 Docker Deployment Examples

### Qwen3/QwQ vLLM Deployment:
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

### Qwen3 with Reasoning Toggle

```csharp
using Microsoft.Extensions.AI;

[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.1 ? "It's sunny" : "It's raining";

IChatClient vllmclient = new VllmQwen3ChatClient("http://localhost:8000/{0}/{1}", null, "qwen3");
IChatClient client = new ChatClientBuilder(vllmclient)
    .UseFunctionInvocation()
    .Build();

var messages = new List<ChatMessage>
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
await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
{
    res += update.Text;
}
```

### QwQ with Full Reasoning Support

```csharp
using Microsoft.Extensions.AI;

[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

IChatClient vllmclient = new VllmQwqChatClient("http://localhost:8000/{0}/{1}", null, "qwq");

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User, "今天天气如何？")
};

ChatOptions chatOptions = new()
{
    Tools = [AIFunctionFactory.Create(GetWeather)]
};

// Stream with reasoning separation
private async Task<(string answer, string reasoning)> StreamChatResponseAsync(
    List<ChatMessage> messages, ChatOptions chatOptions)
{
    string answer = string.Empty;
    string reasoning = string.Empty;
    
    await foreach (var update in vllmclient.GetStreamingResponseAsync(messages, chatOptions))
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

var (answer, reasoning) = await StreamChatResponseAsync(messages, chatOptions);
```

### DeepSeek-R1 with Reasoning

```csharp
using Microsoft.Extensions.AI;

IChatClient client = new VllmDeepseekR1ChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", 
    "your-api-key", 
    "deepseek-r1");

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User, "你是谁？")
};

string res = string.Empty;
string think = string.Empty;

await foreach (ReasoningChatResponseUpdate update in client.GetStreamingResponseAsync(messages))
{
    if (update.Thinking)
    {
        think += update.Text;
    }
    else
    {
        res += update.Text;
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

ChatOptions options = new()
{
    Tools = [AIFunctionFactory.Create(Search)],
    Temperature = 0.7f
};

await foreach (var update in client.GetStreamingResponseAsync(messages, options))
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

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

---

## 📄 License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
