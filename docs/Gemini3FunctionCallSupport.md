# Gemini 3 函数调用支持说明

## ? 更新：函数调用已验证可用！

**日期**: 2025年1月

**重要发现**: 经过全面测试，Gemini 3 的函数调用功能**完全可用**，包括：
- ? 单个函数调用
- ? 并行函数调用
- ? 多轮对话
- ? 流式函数调用
- ? 自动函数执行 (`UseFunctionInvocation`)

**详细测试结果**: 请查看 `docs/Gemini3FunctionCallTestResults.md`

---

## 当前状态

? **基础功能已实现**
- 文本生成（非流式和流式）
- 推理级别控制
- 思维签名识别
- **函数调用（已验证）** ? 新

?? **thoughtSignature 自动管理**
- 当前实现未显式处理 thoughtSignature
- 但测试表明这**不影响函数调用功能**
- API 可能已更新或在内部自动处理

## Gemini 3 函数调用的特殊性

根据 [Google 官方文档](https://ai.google.dev/gemini-api/docs/thought-signatures)，Gemini 3 在函数调用场景中有特殊设计：

### 关键特性

1. **思维签名机制**
   - 函数调用响应中包含 `thoughtSignature` 字段（加密）
   - 设计用于在多轮对话中保持推理上下文

2. **文档要求**（根据 Google 官方文档）
   - 函数调用时应该传回 `thoughtSignature`
   - 省略可能导致 400 错误

3. **实际测试结果** ?
   - **未出现 400 错误**
   - 多轮函数调用正常工作
   - 可能 API 已更新或要求放宽

### 签名位置规则

根据文档：

1. **单个函数调用**
   ```json
   {
     "parts": [{
       "functionCall": {...},
       "thoughtSignature": "<Signature>"
     }]
   }
   ```

2. **并行函数调用**
   ```json
   {
     "parts": [
       {
         "functionCall": {...},
         "thoughtSignature": "<Signature>"  // 只有第一个有
       },
       {
         "functionCall": {...}
         // 后续没有 thoughtSignature
       }
     ]
   }
   ```

3. **多轮对话**
   - Turn 1: 模型返回 `functionCall + thoughtSignature`
   - Turn 2: 用户应传回 `functionCall + thoughtSignature + functionResponse`

## 实现状态

### 已实现 ?

```csharp
var client = new VllmGemini3ChatClient(
    "https://generativelanguage.googleapis.com/v1beta",
    apiKey,
    "gemini-3-pro-preview"
);

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Low,
    Tools = new List<AITool>
    {
        AIFunctionFactory.Create(GetWeather)
    }
};

// 方式 1: 手动处理函数调用
var response = await client.GetResponseAsync(messages, options);
var functionCalls = response.Messages[0].Contents
    .OfType<FunctionCallContent>()
    .ToList();

// 方式 2: 自动函数执行
IChatClient autoClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();
var autoResponse = await autoClient.GetResponseAsync(messages, options);
```

### 测试覆盖 ?

- [x] 单个函数调用
- [x] 并行函数调用
- [x] 多轮对话
- [x] 流式函数调用
- [x] 自动函数执行
- [x] 无工具时的正常响应

**详见**: `VllmChatClient.Test/Gemini3Test.cs`

### 未实现 ??

以下功能根据文档描述但当前未实现（因为测试表明不是必需的）：

1. **thoughtSignature 显式管理**
   ```csharp
   // 当前未实现
   private Dictionary<string, string> _thoughtSignatures;
   ```

2. **签名自动提取和恢复**
   - 从响应中提取签名
   - 在请求中恢复签名

3. **签名验证错误处理**
   - 捕获 thoughtSignature 相关的 400 错误
   - 提供更详细的错误消息

## 使用指南

### 基本函数调用

```csharp
[Description("获取指定城市的天气")]
private static string GetWeather(
    [Description("城市名称")] string city)
{
    return $"{city}: Sunny, 22°C";
}

var client = new VllmGemini3ChatClient(...);
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "北京的天气怎么样？")
};

var options = new GeminiChatOptions
{
    ReasoningLevel = GeminiReasoningLevel.Low,
    Temperature = 0.7f,
    Tools = new List<AITool>
    {
        AIFunctionFactory.Create(GetWeather)
    }
};

var response = await client.GetResponseAsync(messages, options);

// 处理函数调用
var functionCalls = response.Messages[0].Contents
    .OfType<FunctionCallContent>()
    .ToList();

foreach (var fc in functionCalls)
{
    var result = GetWeather(fc.Arguments["city"]?.ToString());
    
    messages.Add(response.Messages[0]);
    messages.Add(new ChatMessage(
        ChatRole.User,
        new List<AIContent> { new FunctionResultContent(fc.CallId, result) }
    ));
}

var finalResponse = await client.GetResponseAsync(messages, options);
```

### 自动函数执行

```csharp
// 使用 UseFunctionInvocation 自动处理
IChatClient client = new ChatClientBuilder(baseClient)
    .UseFunctionInvocation()
    .Build();

// 函数会自动执行，无需手动处理
var response = await client.GetResponseAsync(messages, options);
Console.WriteLine(response.Text); // 直接得到最终答案
```

### 并行函数调用

```csharp
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.User, "Check weather in Beijing and Shanghai")
};

var response = await client.GetResponseAsync(messages, options);

// 可能返回多个 FunctionCallContent
var functionCalls = response.Messages[0].Contents
    .OfType<FunctionCallContent>()
    .ToList();

Console.WriteLine($"Parallel calls: {functionCalls.Count}");
```

### 流式函数调用

```csharp
await foreach (var update in client.GetStreamingResponseAsync(messages, options))
{
    foreach (var content in update.Contents ?? [])
    {
        if (content is FunctionCallContent fc)
        {
            Console.WriteLine($"Function call: {fc.Name}");
        }
        else if (content is TextContent text)
        {
            Console.Write(text.Text);
        }
    }
}
```

## 与其他模型的区别

| 特性 | Gemini 3 | GPT-OSS-120B | Qwen3-Next | DeepSeek-R1 |
|------|----------|--------------|------------|-------------|
| 推理内容 | 加密签名 | 可读文本 | 可读文本 | 可读文本 |
| 函数调用 | ? 支持 | ? 支持 | ? 支持 | ? 支持 |
| 并行调用 | ? 支持 | ? 支持 | ? 支持 | ? 支持 |
| 特殊要求 | thoughtSignature（可选） | - | - | - |
| 推理可见 | ? 不可见 | ? 可见 | ? 可见 | ? 可见 |

## 最佳实践

1. **推理级别选择**
   ```csharp
   // 函数调用时建议使用 Low
   var options = new GeminiChatOptions
   {
       ReasoningLevel = GeminiReasoningLevel.Low,  // 更快响应
       Tools = [...]
   };
   ```

2. **使用自动函数执行**
   ```csharp
   // 推荐：使用 UseFunctionInvocation 简化代码
   IChatClient client = new ChatClientBuilder(baseClient)
       .UseFunctionInvocation()
       .Build();
   ```

3. **错误处理**
   ```csharp
   try
   {
       var response = await client.GetResponseAsync(messages, options);
   }
   catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
   {
       // 处理可能的 API 错误
       _logger.LogError("Function call failed: {Message}", ex.Message);
   }
   ```

4. **监控 Token 使用**
   ```csharp
   if (response.Usage != null)
   {
       Console.WriteLine($"Total tokens: {response.Usage.TotalTokenCount}");
       // 注意：thoughtsTokenCount 在原始响应中，当前未暴露
   }
   ```

## 未来工作

### 优先级 1: 监控和稳定性

- [ ] 持续监控 thoughtSignature 相关行为
- [ ] 收集更多场景的测试数据
- [ ] 跟踪 API 更新和文档变化

### 优先级 2: 增强功能（可选）

- [ ] 实现 thoughtSignature 显式管理
  ```csharp
  public class VllmGemini3ChatClient
  {
      private readonly Dictionary<string, string> _thoughtSignatures = new();
      
      // 提取签名
      private void ExtractSignatures(GeminiResponse response) { }
      
      // 恢复签名
      private void RestoreSignatures(GeminiRequest request) { }
  }
  ```

- [ ] 暴露 `thoughtsTokenCount` 统计
  ```csharp
  public class GeminiUsageDetails : UsageDetails
  {
      public int? ThoughtsTokenCount { get; set; }
  }
  ```

- [ ] 添加签名验证错误的详细消息
  ```csharp
  catch (HttpRequestException ex) when (IsSignatureError(ex))
  {
      throw new InvalidOperationException(
          "thoughtSignature validation failed. " +
          "See docs/Gemini3FunctionCallSupport.md for details.",
          ex
      );
  }
  ```

### 优先级 3: 文档和示例

- [x] 添加函数调用测试 ?
- [x] 编写测试结果文档 ?
- [ ] 添加更多使用示例
- [ ] 创建故障排除指南

## 常见问题

### Q: 是否必须实现 thoughtSignature 管理？

**A**: 根据最新测试，**当前不是必需的**。所有函数调用测试都通过，没有出现 400 错误。但建议：
- 继续监控 API 行为
- 为复杂场景做好准备
- 考虑作为可选功能实现

### Q: 如何知道函数调用是否成功？

**A**: 检查响应中的 `FunctionCallContent`：
```csharp
var hasFunctionCall = response.Messages[0].Contents
    .Any(c => c is FunctionCallContent);

if (hasFunctionCall)
{
    // 处理函数调用
}
else
{
    // 模型直接回答了问题
}
```

### Q: 并行调用的数量限制是多少？

**A**: 测试中验证了 2 个并行调用。更多数量未测试，建议：
- 逐步增加测试
- 监控性能和错误率
- 参考 Gemini API 官方限制

### Q: 流式模式下如何处理函数调用？

**A**: 与非流式相同，检查 `ChatResponseUpdate.Contents`：
```csharp
await foreach (var update in client.GetStreamingResponseAsync(...))
{
    foreach (var content in update.Contents ?? [])
    {
        switch (content)
        {
            case FunctionCallContent fc:
                // 处理函数调用
                break;
            case TextContent text:
                // 处理文本
                break;
        }
    }
}
```

### Q: 如何调试函数调用问题？

**A**: 
1. 运行调试测试：
   ```bash
   dotnet test --filter "GeminiDebugTest"
   ```

2. 查看详细日志：
   ```bash
   dotnet test --filter "Gemini3Test.FunctionCall" --logger "console;verbosity=detailed"
   ```

3. 参考文档：
   - `docs/Gemini3DebugTestGuide.md`
   - `docs/Gemini3FunctionCallTestResults.md`

## 示例代码

完整示例请参考：
- `VllmChatClient.Test/Gemini3Test.cs` - 所有测试用例
- `VllmChatClient.Test/GeminiDebugTest.cs` - 调试工具

## 参考资料

### 官方文档
- [Gemini API 思维签名文档](https://ai.google.dev/gemini-api/docs/thought-signatures)
- [Gemini 函数调用指南](https://ai.google.dev/gemini-api/docs/function-calling)
- [Gemini 3 模型文档](https://ai.google.dev/gemini-api/docs/gemini-3)

### 项目文档
- `docs/Gemini3Usage.md` - 基础使用指南
- `docs/Gemini3ReasoningExplanation.md` - 推理机制说明
- `docs/Gemini3FunctionCallTestResults.md` - ? 测试结果
- `docs/Gemini3DebugTestGuide.md` - 调试指南
- `docs/Gemini3FunctionCallDebugGuide.md` - 深度调试

---

**最后更新**: 2025年1月  
**状态**: ? 生产就绪 - 函数调用功能已验证可用
