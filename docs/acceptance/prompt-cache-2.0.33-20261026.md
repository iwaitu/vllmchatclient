# VllmChatClient 2.0.33 缓存验收（2026-10-26）

修复范围：原生 Anthropic 显式缓存配置、共享缓存用量解析与三协议流式聚合；由 Claude 实施，Root 独立执行验收。默认不启用缓存。没有修改 QRE、RuleMaker、IA.Engine 或既有六模型对照批次。

## 已完成的本地验收

- 新增纯 fake 用例 108/108 通过；精确离线白名单合计 607/607 通过，没有执行依赖真实提供商的 SDK 测试类。
- 实际 Windows NativeAOT 编译并执行通过，JSON 反射关闭；覆盖缓存配置线上字段、Anthropic 流式/非流式、Claude Chat 非流式、Responses 流式缓存用量，并保留 Sonnet opaque 回放与 DeepSeek JSON Object 的原有冒烟。
- 构建和测试使用去提供商进程环境变量、默认外网代理拒绝的独立子进程；真实提供商验证单独由 Root 执行。

## 真实 Claude 缓存验证

官方 `api.anthropic.com/v1/messages`，模型 `claude-sonnet-5-5`，显式 `cache_control={type:ephemeral,ttl:5m}`。合成固定前缀，同样输入顺序调用两次，输出上限128；没有工具轮、重试或协议切换。

| 次序 | 普通输入 | 缓存创建 | 缓存读取 | 输出 | SDK总输入 | SDK总token |
|---|---:|---:|---:|---:|---:|---:|
| 首次 | 4 | 8,724 | 0 | 4 | 8,728 | 8,732 |
| 第二次 | 4 | 0 | 8,724 | 4 | 8,728 | 8,732 |

两次真实转发，首次缓存创建和随后缓存命中均由原始 usage 确认；SDK 标准输入/输出/总数以及四个原始核心计量字段逐项与原始响应一致。不是仅依据HTTP200或调用成功判定。

原始请求/响应与逐笔hash留在本地私密验收目录，不发布凭据、内容、思考或签名。此验证使用由本次源码构建的2.0.33本地DLL；构建时Git基线为a61aac9，源码变动尚未提交，不能冒充已发布包。正式包的来源及独立消费者验收在发布后另行记录。

## 使用和语义

```csharp
var options = new ChatOptions
{
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        [VllmAnthropicCacheControl.OptionKey] = new VllmAnthropicCacheControl("5m")
    }
};
```

客户端须采用 `VllmApiMode.AnthropicMessages`。原始 `input_tokens` 在 Anthropic 中不含缓存，SDK `InputTokenCount` 包含普通输入、缓存读和缓存写。Chat/Responses 的输入本来包含缓存，不能再加一次。原始细分在 `UsageDetails.AdditionalCounts` 中读取。字段缺失保持未知，明确零保留为零；流式每个正常结束响应只发一次标准 `UsageContent`。

官方依据：[缓存配置与用量字段](https://platform.claude.com/docs/en/build-with-claude/prompt-caching)、[Claude价格](https://platform.claude.com/docs/zh-CN/about-claude/pricing)。本验收不代表第三方网关必定支持相同缓存策略，也不代表既有模型测试已开启缓存。
