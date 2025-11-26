# Gemini 3 调试测试指南

本文档说明 `GeminiDebugTest.cs` 中各个测试的用途和预期输出。

## 测试概览

| 测试名称 | 用途 | 关键观察点 |
|---------|------|-----------|
| `DebugRawGeminiResponse` | 基础推理响应 | `thoughtSignature` 位置和格式 |
| `DebugFunctionCallResponse` | 单个函数调用 | 函数调用中的签名 |
| `DebugParallelFunctionCalls` | 并行函数调用 | 签名只在第一个 part |
| `DebugMultiTurnWithFunctionCall` | 多轮对话 | 签名传递验证 |

## 测试详解

### 1. DebugRawGeminiResponse

**目的**：检查基础推理响应的结构

**预期输出**：
```json
{
  "candidates": [{
    "content": {
      "parts": [{
        "text": "答案内容",
        "thoughtSignature": "加密的签名字符串"
      }]
    }
  }],
  "usageMetadata": {
    "thoughtsTokenCount": 798,
    "promptTokenCount": 12,
    "candidatesTokenCount": 249
  }
}
```

**关键点**：
- ? 检查是否有 `thoughtSignature` 字段
- ? 验证 `thoughtsTokenCount` 值
- ? 确认签名是加密的字符串

### 2. DebugFunctionCallResponse

**目的**：检查单个函数调用的响应结构

**预期输出**：
```json
{
  "candidates": [{
    "content": {
      "parts": [{
        "functionCall": {
          "name": "get_weather",
          "args": { "location": "Beijing" }
        },
        "thoughtSignature": "<Signature_A>"
      }]
    }
  }]
}
```

**关键点**：
- ? `functionCall` 和 `thoughtSignature` 在同一个 part
- ? 签名必须存在（Gemini 3 强制要求）
- ?? 如果缺少签名，下一轮请求会失败

### 3. DebugParallelFunctionCalls

**目的**：验证并行函数调用的签名行为

**测试用例**：`"Check the weather in Paris and London"`

**预期输出**：
```json
{
  "candidates": [{
    "content": {
      "parts": [
        {
          "functionCall": {
            "name": "get_current_temperature",
            "args": { "location": "Paris" }
          },
          "thoughtSignature": "<Signature_A>"  // ? 只有第一个有签名
        },
        {
          "functionCall": {
            "name": "get_current_temperature",
            "args": { "location": "London" }
          }
          // ? 第二个没有 thoughtSignature
        }
      ]
    }
  }]
}
```

**验证点**：
- ? Part 0: 有 `thoughtSignature`
- ? Part 1: 无 `thoughtSignature`
- ?? 如果其他 part 有签名，这是非预期行为

**根据文档**：
> 并行函数调用时，`thoughtSignature` 仅附加到第一个 `functionCall` 部分

### 4. DebugMultiTurnWithFunctionCall

**目的**：测试多轮对话中的签名传递

**流程**：

#### Turn 1: 用户请求
```json
{
  "contents": [{
    "role": "user",
    "parts": [{ "text": "What's the weather in Tokyo?" }]
  }]
}
```

#### Turn 1: 模型响应（函数调用）
```json
{
  "candidates": [{
    "content": {
      "parts": [{
        "functionCall": {
          "name": "get_weather",
          "args": { "city": "Tokyo" }
        },
        "thoughtSignature": "<Signature_X>"  // ? 提取这个
      }]
    }
  }]
}
```

#### Turn 2: 返回函数结果（必须带签名）
```json
{
  "contents": [
    {
      "role": "user",
      "parts": [{ "text": "What's the weather in Tokyo?" }]
    },
    {
      "role": "model",
      "parts": [{
        "functionCall": {
          "name": "get_weather",
          "args": { "city": "Tokyo" }
        },
        "thoughtSignature": "<Signature_X>"  // ? 必须原样传回
      }]
    },
    {
      "role": "user",
      "parts": [{
        "functionResponse": {
          "name": "get_weather",
          "response": { "temperature": "22°C" }
        }
      }]
    }
  ]
}
```

**预期结果**：
- ? 200 OK：签名正确传回
- ? 400 Bad Request：签名缺失或错误

**错误示例**：
```
400 Bad Request: Function call 'get_weather' missing 'thought_signature'
```

## 运行测试

### 设置 API Key
```bash
# Windows PowerShell
$env:GEMINI_API_KEY="your-api-key-here"

# Linux/macOS
export GEMINI_API_KEY="your-api-key-here"
```

### 运行所有调试测试
```bash
dotnet test --filter "FullyQualifiedName~GeminiDebugTest"
```

### 运行特定测试
```bash
# 基础推理测试
dotnet test --filter "FullyQualifiedName~DebugRawGeminiResponse"

# 函数调用测试
dotnet test --filter "FullyQualifiedName~DebugFunctionCallResponse"

# 并行函数调用
dotnet test --filter "FullyQualifiedName~DebugParallelFunctionCalls"

# 多轮对话
dotnet test --filter "FullyQualifiedName~DebugMultiTurnWithFunctionCall"
```

### 查看详细输出
```bash
dotnet test --filter "FullyQualifiedName~GeminiDebugTest" --logger "console;verbosity=detailed"
```

## 输出分析

### ? 成功标记

测试输出中会显示：

```
? Found thoughtSignature: VhL8ZqA3T...
? Found functionCall
  Function name: get_weather
? Has thoughtSignature: <Signature_A>...
? Total parts: 2
Expected behavior: Only first part should have thoughtSignature
? Multi-turn function call with signature successful!
```

### ?? 警告标记

可能出现的警告：

```
?? WARNING: Signature found in non-first part (unexpected for parallel calls)
?? WARNING: First function call part missing signature
?? No thoughtSignature found in first response
```

### ? 错误标记

如果测试失败：

```
? HTTP Error: Bad Request
This might be due to missing or incorrect thoughtSignature
```

## 实现参考

基于这些测试结果，实现函数调用时需要：

### 1. 响应解析
```csharp
public class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("thoughtSignature")]  // ? 必须包含
    public string? ThoughtSignature { get; set; }

    [JsonPropertyName("functionCall")]
    public GeminiFunctionCall? FunctionCall { get; set; }
}
```

### 2. 签名保存
```csharp
// 从响应中提取签名
foreach (var part in content.Parts)
{
    if (!string.IsNullOrEmpty(part.ThoughtSignature))
    {
        // 保存签名供下一轮使用
        _thoughtSignatures[part.FunctionCall?.Name ?? ""] = part.ThoughtSignature;
    }
}
```

### 3. 请求构建
```csharp
// 构建请求时恢复签名
if (_thoughtSignatures.TryGetValue(functionCallName, out var signature))
{
    parts.Add(new GeminiPart
    {
        FunctionCall = functionCall,
        ThoughtSignature = signature  // ? 必须包含
    });
}
```

## 常见问题

### Q: 为什么 thoughtSignature 是加密的？

**A**: Gemini 3 的设计理念是将推理过程内部化。签名是加密的上下文标记，用于在多轮对话中保持推理状态，不是给用户阅读的。

### Q: 可以跳过 thoughtSignature 吗？

**A**: 不行！在函数调用场景中，如果省略 `thoughtSignature`，Gemini API 会返回 400 错误。

### Q: 并行函数调用时，为什么只有第一个有签名？

**A**: 根据 Google 文档，并行调用共享同一个推理上下文，因此只需要一个签名。在返回结果时，也只需要在第一个函数调用 part 中包含签名。

### Q: 如何验证签名是否正确传递？

**A**: 运行 `DebugMultiTurnWithFunctionCall` 测试。如果第二轮请求成功（200 OK），说明签名传递正确；如果失败（400 Bad Request），检查签名是否完整且位置正确。

## 后续步骤

1. **验证当前行为**
   ```bash
   dotnet test --filter "GeminiDebugTest"
   ```

2. **分析输出**
   - 确认 `thoughtSignature` 存在
   - 记录签名格式和长度
   - 验证并行调用行为

3. **实现自动管理**
   - 基于测试结果实现签名提取
   - 实现签名存储机制
   - 实现签名恢复逻辑

4. **集成测试**
   - 在 `VllmGemini3ChatClient` 中实现
   - 添加端到端函数调用测试
   - 验证与 `UseFunctionInvocation` 的兼容性

## 参考资料

- [Gemini API 思维签名文档](https://ai.google.dev/gemini-api/docs/thought-signatures)
- [Gemini 函数调用指南](https://ai.google.dev/gemini-api/docs/function-calling)
- `docs/Gemini3FunctionCallSupport.md` - 函数调用支持说明
