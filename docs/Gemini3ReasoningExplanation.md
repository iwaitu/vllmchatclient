# Gemini 3 推理功能说明

## 概述

Gemini 3 使用**思维链签名（Thought Signatures）**机制来管理推理上下文，而不是直接暴露可读的推理文本。

## 关键特点

### 1. 思维签名是加密的
- `thoughtSignature` 字段包含**加密的推理表示**，不是可读的推理文本
- 用于在多轮对话中保持推理上下文
- 实际的推理过程是模型内部的，对用户不可见

### 2. 推理 Token 统计
尽管推理文本不可见，但可以通过 `UsageMetadata` 中的 `thoughtsTokenCount` 看到推理使用的 token 数量：

```json
"usageMetadata": {
  "promptTokenCount": 12,
  "candidatesTokenCount": 249,
  "totalTokenCount": 1059,
  "thoughtsTokenCount": 798  // 推理使用了 798 个 token
}
```

### 3. 推理级别控制
通过 `GeminiChatOptions` 控制推理级别：

```csharp
var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Normal,  // 或 Low
    Temperature = 1.0f
};
```

- **Normal（High）**: 默认级别，进行深度推理
- **Low**: 快速响应模式，减少推理开销

## 实现细节

### 响应中的思维签名
```json
{
  "candidates": [{
    "content": {
      "parts": [{
        "text": "答案文本",
        "thoughtSignature": "加密的签名字符串"
      }]
    },
    "finishReason": "STOP"
  }],
  "usageMetadata": {
    "thoughtsTokenCount": 798
  }
}
```

### 在多轮对话中的作用
根据 [Google 官方文档](https://ai.google.dev/gemini-api/docs/thought-signatures)：

1. **函数调用时必须传回**: 在使用函数调用时，必须将 `thoughtSignature` 原样传回，否则会收到 400 错误
2. **非函数调用场景**: 建议传回签名以保持推理质量，但不强制
3. **验证规则**: API 会验证当前轮次中的所有函数调用签名

## C# 客户端实现

### ReasoningChatResponse
```csharp
if (response is ReasoningChatResponse reasoningResponse)
{
    // Gemini 3 的 Reason 字段会显示类似：
    // "[Gemini 3 内部推理 - 1 个思维签名]"
    Console.WriteLine($"Reasoning Note: {reasoningResponse.Reason}");
}
```

### 流式响应
```csharp
await foreach (var update in client.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate reasoningUpdate)
    {
        if (reasoningUpdate.Thinking)
        {
            // 会显示 "[Gemini 3 思维签名]"
            Console.WriteLine($"[Internal Reasoning]");
        }
        else
        {
            Console.Write(update.Text);
        }
    }
}
```

## 与其他模型的区别

| 模型 | 推理内容 | 是否可读 |
|------|----------|----------|
| **Gemini 3** | 加密的 `thoughtSignature` | ? 不可读 |
| GPT-OSS-120B | `reasoning` 文本字段 | ? 完全可读 |
| Qwen3-Next (thinking) | `reasoningContent` 文本 | ? 完全可读 |
| DeepSeek-R1 | `<think>` 标签包裹的文本 | ? 完全可读 |

## 最佳实践

1. **不要尝试解密签名**: 签名是加密的，用于内部上下文管理
2. **使用 thoughtsTokenCount**: 通过 token 统计了解推理开销
3. **调整推理级别**: 根据场景选择 Normal 或 Low
4. **函数调用场景**: 确保正确传回签名（SDK 会自动处理）

## 示例代码

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;

var client = new VllmGemini3ChatClient(
    "https://generativelanguage.googleapis.com/v1beta",
    "YOUR_API_KEY",
    "gemini-3-pro-preview"
);

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "What is 2+2? Explain your reasoning.")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Normal,
    Temperature = 1.0f
};

var response = await client.GetResponseAsync(messages, options);

Console.WriteLine($"Response: {response.Text}");
Console.WriteLine($"Reasoning Info: {((ReasoningChatResponse)response).Reason}");
Console.WriteLine($"Total Tokens: {response.Usage?.TotalTokenCount}");
// 注意：具体的 thoughtsTokenCount 在 UsageMetadata 中，但当前 SDK 不直接暴露
```

## 参考资料

- [Gemini API 思维签名文档](https://ai.google.dev/gemini-api/docs/thought-signatures?hl=zh-cn)
- [Gemini 3 开发者指南](https://ai.google.dev/gemini-api/docs/gemini-3?hl=zh-cn)
- [Gemini 思考型模型](https://ai.google.dev/gemini-api/docs/thinking?hl=zh-cn)
