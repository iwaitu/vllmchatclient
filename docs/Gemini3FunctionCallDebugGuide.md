# Gemini 3 函数调用调试指南

## 概述

为了支持 Gemini 3 的函数调用功能，我们添加了一系列调试测试来验证 `thoughtSignature` 的行为。这些测试直接调用 Gemini API，用于理解和验证签名机制。

## 文件结构

```
VllmChatClient.Test/
├── GeminiDebugTest.cs          # 调试测试实现
└── docs/
    ├── Gemini3DebugTestGuide.md         # 测试指南
    ├── Gemini3FunctionCallSupport.md    # 函数调用支持说明
    └── Gemini3ReasoningExplanation.md   # 推理机制说明
```

## 调试测试说明

### 测试列表

| 测试方法 | 目的 | 关键点 |
|---------|------|--------|
| `DebugRawGeminiResponse` | 验证基础推理响应 | 检查 `thoughtSignature` 存在性 |
| `DebugFunctionCallResponse` | 测试单个函数调用 | 验证函数调用中的签名位置 |
| `DebugParallelFunctionCalls` | 测试并行函数调用 | 验证只有第一个 part 有签名 |
| `DebugMultiTurnWithFunctionCall` | 测试多轮对话 | 验证签名必须传回 |

### 快速开始

#### 1. 设置环境变量

```bash
# Windows PowerShell
$env:GEMINI_API_KEY="your-gemini-api-key"

# Linux/macOS
export GEMINI_API_KEY="your-gemini-api-key"
```

#### 2. 运行测试

```bash
# 运行所有调试测试
dotnet test --filter "FullyQualifiedName~GeminiDebugTest" --logger "console;verbosity=detailed"

# 运行特定测试
dotnet test --filter "FullyQualifiedName~DebugFunctionCallResponse" --logger "console;verbosity=detailed"
```

#### 3. 分析输出

测试会输出详细的 JSON 响应，包括：

```
=== RAW GEMINI RESPONSE ===
{
  "candidates": [{
    "content": {
      "parts": [{
        "functionCall": { ... },
        "thoughtSignature": "..."  // ? 关键字段
      }]
    }
  }]
}

=== ANALYZING STRUCTURE ===
--- Part 0 ---
? Found thoughtSignature: VhL8ZqA3T...
? Found functionCall
  Function name: get_weather
```

## 关键发现

### 1. 思维签名位置

**单个函数调用**：
```json
{
  "parts": [{
    "functionCall": { "name": "get_weather", ... },
    "thoughtSignature": "<Signature>"  // ? 在同一个 part
  }]
}
```

**并行函数调用**：
```json
{
  "parts": [
    {
      "functionCall": { "name": "get_weather", "args": { "location": "Paris" } },
      "thoughtSignature": "<Signature>"  // ? 只有第一个有
    },
    {
      "functionCall": { "name": "get_weather", "args": { "location": "London" } }
      // ? 第二个没有 thoughtSignature
    }
  ]
}
```

### 2. 签名传递要求

**Turn 1: 模型响应**
```json
{
  "role": "model",
  "parts": [{
    "functionCall": { "name": "get_weather", ... },
    "thoughtSignature": "<Signature_A>"  // ? 记录这个
  }]
}
```

**Turn 2: 用户请求（必须包含签名）**
```json
{
  "contents": [
    { "role": "user", ... },
    {
      "role": "model",
      "parts": [{
        "functionCall": { "name": "get_weather", ... },
        "thoughtSignature": "<Signature_A>"  // ? 必须原样传回
      }]
    },
    {
      "role": "user",
      "parts": [{
        "functionResponse": { ... }
      }]
    }
  ]
}
```

**如果缺少签名**：
```
? 400 Bad Request
Error: Function call 'get_weather' missing 'thought_signature'
```

### 3. Token 统计

```json
{
  "usageMetadata": {
    "promptTokenCount": 12,
    "candidatesTokenCount": 249,
    "totalTokenCount": 1059,
    "thoughtsTokenCount": 798  // ? 推理使用的 token
  }
}
```

## 实现建议

基于调试测试的发现，实现函数调用支持需要：

### 1. 扩展模型类

```csharp
internal class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("thoughtSignature")]  // ? 必需
    public string? ThoughtSignature { get; set; }

    [JsonPropertyName("functionCall")]
    public GeminiFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("functionResponse")]
    public GeminiFunctionResponse? FunctionResponse { get; set; }
}
```

### 2. 签名管理策略

**方案 A：内部字典管理（推荐）**
```csharp
public class VllmGemini3ChatClient : IChatClient
{
    // 维护 callId -> thoughtSignature 的映射
    private readonly Dictionary<string, string> _thoughtSignatures = new();

    private ChatResponse FromGeminiResponse(GeminiResponse response)
    {
        foreach (var part in content.Parts)
        {
            // 保存签名
            if (!string.IsNullOrEmpty(part.ThoughtSignature) && 
                part.FunctionCall != null)
            {
                var callId = GenerateCallId();
                _thoughtSignatures[callId] = part.ThoughtSignature;
                
                contents.Add(new FunctionCallContent(
                    callId,
                    part.FunctionCall.Name,
                    part.FunctionCall.Args
                ));
            }
        }
    }

    private GeminiContent ToGeminiContent(ChatMessage message)
    {
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent fcc)
            {
                // 恢复签名
                var signature = _thoughtSignatures.GetValueOrDefault(fcc.CallId);
                parts.Add(new GeminiPart
                {
                    FunctionCall = new GeminiFunctionCall { ... },
                    ThoughtSignature = signature  // ? 必须包含
                });
            }
        }
    }
}
```

**方案 B：扩展 FunctionCallContent**
```csharp
// 需要在 Microsoft.Extensions.AI 中支持 AdditionalProperties
var functionCall = new FunctionCallContent(...)
{
    AdditionalProperties = new Dictionary<string, object?>
    {
        ["thoughtSignature"] = part.ThoughtSignature
    }
};
```

### 3. 并行调用处理

```csharp
private ChatResponse FromGeminiResponse(GeminiResponse response)
{
    string? sharedSignature = null;
    bool isFirstFunctionCall = true;

    foreach (var part in content.Parts)
    {
        if (part.FunctionCall != null)
        {
            // 只保存第一个函数调用的签名
            if (isFirstFunctionCall && !string.IsNullOrEmpty(part.ThoughtSignature))
            {
                sharedSignature = part.ThoughtSignature;
                isFirstFunctionCall = false;
            }

            var callId = GenerateCallId();
            // 所有并行调用共享同一个签名
            if (sharedSignature != null)
            {
                _thoughtSignatures[callId] = sharedSignature;
            }

            contents.Add(new FunctionCallContent(callId, ...));
        }
    }
}
```

## 验证清单

在实现函数调用支持后，使用以下清单验证：

- [ ] 单个函数调用：签名正确提取
- [ ] 单个函数调用：签名正确传回，无 400 错误
- [ ] 并行函数调用：只有第一个 part 有签名
- [ ] 并行函数调用：所有调用共享同一个签名
- [ ] 多轮对话：签名在每轮正确传递
- [ ] Token 统计：`thoughtsTokenCount` 正确暴露
- [ ] 与 `UseFunctionInvocation` 兼容

## 测试命令参考

```bash
# 1. 验证基础推理（无函数调用）
dotnet test --filter "DebugRawGeminiResponse" -l "console;verbosity=detailed"

# 2. 测试单个函数调用
dotnet test --filter "DebugFunctionCallResponse" -l "console;verbosity=detailed"

# 3. 测试并行函数调用
dotnet test --filter "DebugParallelFunctionCalls" -l "console;verbosity=detailed"

# 4. 测试多轮对话（关键测试）
dotnet test --filter "DebugMultiTurnWithFunctionCall" -l "console;verbosity=detailed"

# 5. 运行所有调试测试
dotnet test --filter "GeminiDebugTest" -l "console;verbosity=detailed"
```

## 常见错误

### 错误 1: 400 Bad Request - Missing thought_signature

**原因**：在多轮对话中未传回 `thoughtSignature`

**解决**：
```csharp
// ? 错误：缺少签名
{
  "role": "model",
  "parts": [{
    "functionCall": { ... }
    // ? 没有 thoughtSignature
  }]
}

// ? 正确：包含签名
{
  "role": "model",
  "parts": [{
    "functionCall": { ... },
    "thoughtSignature": "<Signature_A>"  // ? 必须包含
  }]
}
```

### 错误 2: 并行调用签名位置错误

**原因**：在非第一个 part 添加了签名

**解决**：
```csharp
// ? 错误：所有 part 都有签名
parts = [
  { functionCall: "Paris", thoughtSignature: "A" },
  { functionCall: "London", thoughtSignature: "B" }  // ? 不应该有
]

// ? 正确：只有第一个有签名
parts = [
  { functionCall: "Paris", thoughtSignature: "A" },  // ? 只有这个
  { functionCall: "London" }  // ? 没有签名
]
```

### 错误 3: 签名未保存

**原因**：从响应中未提取签名，导致下一轮无法传回

**解决**：
```csharp
// 在解析响应时保存签名
if (!string.IsNullOrEmpty(part.ThoughtSignature))
{
    _thoughtSignatures[callId] = part.ThoughtSignature;  // ? 必须保存
}
```

## 下一步

1. **运行调试测试**
   ```bash
   dotnet test --filter "GeminiDebugTest"
   ```

2. **查看详细文档**
   - `docs/Gemini3DebugTestGuide.md` - 测试指南
   - `docs/Gemini3FunctionCallSupport.md` - 实现说明

3. **实现函数调用支持**
   - 基于调试测试结果实现签名管理
   - 添加端到端测试
   - 验证与现有工具的兼容性

4. **贡献改进**
   - 如果发现新的边界情况，添加测试
   - 更新文档
   - 提交 PR

## 参考资料

- [Gemini API 思维签名文档](https://ai.google.dev/gemini-api/docs/thought-signatures)
- [Gemini 函数调用指南](https://ai.google.dev/gemini-api/docs/function-calling)
- [Gemini 3 模型文档](https://ai.google.dev/gemini-api/docs/gemini-3)
