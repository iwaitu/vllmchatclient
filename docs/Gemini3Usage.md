# Gemini 3 客户端使用指南

## 概述

`VllmGemini3ChatClient` 是专为 Google Gemini 3 Pro API 设计的客户端实现，支持 Gemini 3 的高级推理功能和思维链（Chain-of-Thought）能力。

## 主要特性

1. **思维级别控制** - 通过 `GeminiReasoningLevel` 控制模型的推理深度
2. **Google API 认证** - 使用 `x-goog-api-key` 头进行认证
3. **推理内容捕获** - 支持捕获和分析模型的思考过程
4. **流式响应** - 支持实时流式接收响应
5. **原生 Gemini API** - 支持 Gemini 原生 API 端点格式

## 安装与配置

### 1. 获取 API 密钥

访问 [Google AI Studio](https://aistudio.google.com/apikey) 获取您的 Gemini API 密钥。

### 2. 创建客户端实例

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;

// 方式 1: 使用简化的基础 URL（推荐）
var client = new VllmGemini3ChatClient(
    endpoint: "https://generativelanguage.googleapis.com/v1beta",
    token: "YOUR_GEMINI_API_KEY",
    modelId: "gemini-3-pro-preview"
);
// 客户端会自动构建完整端点: 
// https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent

// 方式 2: 使用完整的 API URL
var client2 = new VllmGemini3ChatClient(
    endpoint: "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent",
    token: "YOUR_GEMINI_API_KEY",
    modelId: "gemini-3-pro-preview"
);
```

## API 端点说明

### Gemini 原生 API 格式

Gemini 3 使用原生 Google API 格式，与 OpenAI 格式不同：

- **非流式端点**: `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- **流式端点**: `https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent`

客户端会自动：
- 根据基础 URL（如 `v1beta`）构建完整端点
- 在流式请求时将 `:generateContent` 替换为 `:streamGenerateContent`
- 处理 `x-goog-api-key` 认证头

## 使用示例

### 基础对话（Normal 推理级别）

```csharp
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "Find the race condition in this multi-threaded C++ snippet: [code here]")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Normal, // 使用默认的高级推理
    Temperature = 1.0f // Gemini 3 推荐保持默认值 1.0
};

var response = await client.GetResponseAsync(messages, options);

if (response is ReasoningChatResponse reasoningResponse)
{
    Console.WriteLine("=== 推理过程 ===");
    Console.WriteLine(reasoningResponse.Reason);
    Console.WriteLine("\n=== 最终回答 ===");
    Console.WriteLine(reasoningResponse.Text);
}
```

### 低延迟模式（Low 推理级别）

```csharp
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "How does AI work?")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Low, // 低思维级别，更快的响应
    Temperature = 1.0f
};

var response = await client.GetResponseAsync(messages, options);
Console.WriteLine(response.Text);
```

### 流式响应

```csharp
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "Explain quantum computing step by step.")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Normal
};

var result = new StringBuilder();
var reasoning = new StringBuilder();

await foreach (var update in client.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate reasoningUpdate)
    {
        if (reasoningUpdate.Thinking)
        {
            // 捕获思考过程
            reasoning.Append(reasoningUpdate.Reasoning);
            Console.Write($"[Thinking] {reasoningUpdate.Reasoning}");
        }
        else
        {
            // 捕获最终回答
            result.Append(update.Text);
            Console.Write(update.Text);
        }
    }
}

Console.WriteLine("\n\n=== 完整推理过程 ===");
Console.WriteLine(reasoning.ToString());
```

### 结构化输出

```csharp
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "Extract 3 tags from this text: [your text here]")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Low,
    ResponseFormat = new ChatResponseFormatJson
    {
        Schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                tags = new
                {
                    type = "array",
                    items = new { type = "string" }
                }
            }
        })
    }
};

var response = await client.GetResponseAsync(messages, options);
var tags = JsonSerializer.Deserialize<TagsResponse>(response.Text);
```

## 推理级别说明

### `GeminiReasoningLevel.Normal` (默认 High)

- **适用场景**：复杂推理、数学问题、代码分析、逻辑推理
- **特点**：
  - 最大化推理深度
  - 可能有较长的首个 token 延迟
  - 输出经过更仔细的推理
  - 推荐用于需要高质量、准确答案的场景

### `GeminiReasoningLevel.Low`

- **适用场景**：简单问答、聊天、高吞吐量应用
- **特点**：
  - 最小化延迟和成本
  - 更快的响应速度
  - 适合简单的指令遵循任务
  - 推荐用于不需要复杂推理的场景

## API 端点配置

### Google AI Studio (推荐)

```csharp
// 推荐方式：使用基础 URL
var client = new VllmGemini3ChatClient(
    "https://generativelanguage.googleapis.com/v1beta",
    "YOUR_API_KEY",
    "gemini-3-pro-preview"
);

// 或者使用完整 URL
var client2 = new VllmGemini3ChatClient(
    "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent",
    "YOUR_API_KEY",
    "gemini-3-pro-preview"
);
```

### 版本说明

- `v1beta` - Beta 版本，功能最新但可能变化
- `v1alpha` - Alpha 版本，实验性功能
- `v1` - 稳定版本（未来可用）

### 自定义端点

如果使用自托管或代理服务：

```csharp
var client = new VllmGemini3ChatClient(
    "https://your-proxy.com/v1beta",
    "YOUR_API_KEY",
    "gemini-3-pro-preview"
);
```

## 认证说明

### x-goog-api-key 认证

Gemini API 使用 `x-goog-api-key` HTTP 头进行认证，而不是 OAuth 或 Bearer token：

```http
POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent
x-goog-api-key: YOUR_API_KEY
Content-Type: application/json
```

客户端会自动处理：
- 移除任何 `Authorization` 头
- 设置正确的 `x-goog-api-key` 头
- 在请求中包含正确的认证信息

## 最佳实践

### 1. 温度设置

Gemini 3 针对默认温度 1.0 进行了优化，建议保持此值：

```csharp
var options = new GeminiChatOptions
{
    Temperature = 1.0f // 推荐保持默认值
};
```

?? **警告**：降低温度（如 < 1.0）可能导致意外行为，特别是在复杂数学或推理任务中可能出现循环或性能下降。

### 2. 推理级别选择

- **复杂任务** → `GeminiReasoningLevel.Normal`
- **简单任务** → `GeminiReasoningLevel.Low`
- **不确定** → 先用 `Normal`，如果延迟太高再降级

### 3. 提示工程

Gemini 3 是推理模型，提示应该简洁明了：

? **推荐**：
```csharp
"Find the bug in this code: [code]"
```

? **不推荐**（过于复杂）：
```csharp
"You are an expert developer. Please carefully analyze the following code step by step, considering all edge cases, and identify any potential bugs..."
```

### 4. 错误处理

```csharp
try
{
    var response = await client.GetResponseAsync(messages, options);
    // 处理响应
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"API 请求失败: {ex.Message}");
    Console.WriteLine($"状态码: {ex.StatusCode}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"响应处理失败: {ex.Message}");
}
```

## 定价信息

- **Input**: $2/M tokens (<200K) | $4/M tokens (>200K)
- **Output**: $12/M tokens (<200K) | $18/M tokens (>200K)
- **上下文窗口**: 1M tokens (输入) / 64K tokens (输出)

## 限制与注意事项

1. **知识截止日期**: 2025年1月
2. **温度**: 建议保持 1.0，修改可能影响性能
3. **推理级别**: 目前仅支持 Low 和 Normal（默认 High）
4. **工具调用**: 支持标准 OpenAI 格式的函数调用
5. **API 版本**: 使用 v1beta API（可能在未来变化）

## 环境变量配置

```bash
# Windows PowerShell
$env:GEMINI_API_KEY="your-api-key-here"

# Linux/Mac
export GEMINI_API_KEY="your-api-key-here"
```

## 故障排查

### 问题：401 Unauthorized

**可能原因**:
- API 密钥无效或已过期
- API 密钥格式不正确

**解决方案**:
- 访问 [Google AI Studio](https://aistudio.google.com/apikey) 重新生成 API 密钥
- 确认 API 密钥没有多余的空格或换行符
- 检查环境变量设置是否正确

### 问题：404 Not Found

**可能原因**:
- 端点 URL 格式不正确
- 模型 ID 错误

**解决方案**:
- 使用推荐的基础 URL 格式: `https://generativelanguage.googleapis.com/v1beta`
- 确认模型 ID 为 `gemini-3-pro-preview`
- 检查端点是否包含正确的版本号（v1beta）

### 问题：推理内容为空

**可能原因**:
- 某些简单问题可能不触发推理过程
- 使用了 Low 推理级别

**解决方案**:
- 检查是否使用了 `ReasoningChatResponse` 接收响应
- 尝试使用更复杂的问题
- 切换到 Normal 推理级别

### 问题：响应速度慢

**可能原因**:
- 使用了 Normal（高）推理级别
- 网络延迟
- 问题过于复杂

**解决方案**:
- 尝试降低推理级别到 `Low`
- 检查网络连接
- 考虑使用流式响应以获得渐进式输出
- 简化问题描述

### 问题：Vllm error: (空消息)

**可能原因**:
- 请求格式不正确
- 端点配置错误

**解决方案**:
- 确保使用基础 URL 而不是完整 URL with chat/completions
- 检查是否正确设置了 x-goog-api-key
- 启用调试测试查看详细错误信息

```csharp
// 运行调试测试
await client.DebugApiConnection();
```

## 调试技巧

### 启用详细日志

```csharp
// 创建带有日志的 HttpClient
var handler = new HttpClientHandler();
var loggingHandler = new LoggingHandler(handler);
var httpClient = new HttpClient(loggingHandler);

var client = new VllmGemini3ChatClient(
    "https://generativelanguage.googleapis.com/v1beta",
    "YOUR_API_KEY",
    "gemini-3-pro-preview",
    httpClient
);
```

### 检查请求详情

```csharp
try
{
    var response = await client.GetResponseAsync(messages, options);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    }
}
```

## 参考资源

- [Gemini 3 官方文档](https://ai.google.dev/gemini-api/docs/gemini-3)
- [Google AI Studio](https://aistudio.google.com/)
- [Gemini API 参考](https://ai.google.dev/api)
- [Gemini API 快速开始](https://ai.google.dev/gemini-api/docs/quickstart)
