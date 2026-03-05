# vllmchatclient

[![GitHub stars](https://img.shields.io/github/stars/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/iwaitu/vllmchatclient?style=social)](https://github.com/iwaitu/vllmchatclient/network)
[![GitHub issues](https://img.shields.io/github/issues/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/issues)
[![GitHub license](https://img.shields.io/github/license/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/blob/master/LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/iwaitu/vllmchatclient)](https://github.com/iwaitu/vllmchatclient/commits/main)
[![.NET](https://img.shields.io/badge/platform-.NET%208-blueviolet)](https://dotnet.microsoft.com/)

# C# vLLM Chat Client

A comprehensive .NET 8 chat client library that supports various LLM models including **OpenAI GPT 系列**, **Claude 4.6 / 4.5**, **GPT-OSS-120B**, **Qwen3**, **Qwen3-Next**, **Qwen 3.5**, **QwQ-32B**, **Gemma3**, **DeepSeek-R1**, **DeepSeek-V3.2**, **Kimi K2 / Kimi 2.5**, **GLM-5 / GLM 4.6 / 4.7 / 4.7 Flash / 4.5**, **Gemini 3**, **MiniMax-M2.5** with advanced reasoning capabilities.


## 🚀 Features

- ✅ **Multi-model Support**: OpenAI GPT 系列, Claude 4.6 / 4.5, Qwen3, Qwen3-Next, Qwen 3.5 (supports multiple modelIds, including Qwen3-VL), QwQ, Gemma3, DeepSeek-R1, DeepSeek-V3.2, GLM-5 / GLM-4 / glm-4.6 / glm-4.7 / glm-4.7-flash / glm-4.5, GPT-OSS-120B/20B, Kimi K2 / Kimi 2.5, Gemini 3, MiniMax-M2.5

- ✅ **Reasoning Chain Support**: Built-in thinking/reasoning capabilities for supported models (GLM supports Zhipu official thinking parameter via `GlmChatOptions.ThinkingEnabled`)
- ✅ **Stream Function Calls**: Real-time function calling with streaming responses
- ✅ **Multiple Deployment Options**: Local vLLM deployment and cloud API support
- ✅ **Performance Optimized**: Efficient streaming and memory management
- ✅ **.NET 8 Ready**: Full compatibility with the latest .NET platform

## 📦 Project Repository

**GitHub**: https://github.com/iwaitu/vllmchatclient

---

## 本次更新

### 🆕 Claude 4.6 / 4.5 思维链支持
  
- **新增 `VllmClaudeChatClient`**：专门适配 OpenRouter 等平台提供的 Claude 模型。
- **思维链参数适配**：支持 Claude 4.6 推出的 `reasoning: { effort: "high"|"medium"|"low" }` 参数（通过 `VllmChatOptions.ThinkingEnabled = true` 开启，默认使用 `high`）。
- **响应格式解析**：支持从模型返回的 `reasoning` 字符串或 `reasoning_details` 数组中提取思维链内容，并统一封装进 `ReasoningChatResponse`。
- **Token 优化**：针对 Claude 默认较大的 token 限制进行了保护性设置，避免 OpenRouter 额度报错。

### 🆕 OpenAI GPT 系列支持
  
- **新增 `VllmOpenAiGptClient`**：专门适配 OpenAI 官方或 OpenRouter 提供的 GPT 系列模型（如 gpt-4o, gpt-5.2-codex 等）。
- **推理分段支持**：支持包含思维链的 GPT 系列模型，通过 `OpenAiGptChatOptions` 控制推理级别 (`ReasoningLevel`)。
- **灵活配置**：内置 `ExcludeReasoning` 选项，允许控制是否在输出中包含推理过程。

### 🆕 DeepSeek V3.2 思维链支持

- **`VllmDeepseekV3ChatClient` 思维链修复**：
  - 修正请求格式：DashScope API 使用 `enable_thinking: true`（顶层布尔值），而非 Kimi 格式的 `thinking: {type: "enabled"}`。
  - 模型返回的 `reasoning_content` 字段现在可以正确解析并输出。
  - 非流式响应通过 `ReasoningChatResponse.Reason` 获取思维链内容。
  - 流式响应通过 `ReasoningChatResponseUpdate.Thinking` 区分思考阶段与最终回答。
  - 支持通过 `VllmChatOptions.ThinkingEnabled = true` 开启思维链。
  - 兼容 DashScope 平台 `deepseek-v3.2` 模型。

### 🐛 Bug Fixes

- **`VllmGptOssChatClient` 流式函数调用 Bug 修复**：
  - 修复了流式手动函数调用（Manual Function Call）时，模型返回 `tool_calls` 后第一个流结束、导致无法获取最终文本回复的问题。
  - 新增 `GetStreamingResponseAsync` 重写：自动检测调用方已将工具结果追加到 `messages`，并自动发起第二轮流式请求，实现无缝的工具调用 → 最终回复流程。
  - 现在 `StreamChatManualFunctionCallTest` 可以在单个 `await foreach` 循环中完成完整的工具调用流程，无需手动编写 "Second turn" 逻辑。
  - 简化了默认系统提示词，去除了"tool_calls 时 content 必须为空"的硬性约束。

### 🔄 `VllmQwen3NextChatClient` 重构 — 统一多模型适配

- **`VllmQwen3NextChatClient` 已适配多个模型系列**，通过构造函数 `modelId` 或 `ChatOptions.ModelId` 切换，无需再使用独立的 Client 类：
  - `qwen3.5-397b-a17b`（Qwen 3.5，最新）
  - `qwen3-next-80b-a3b-thinking` / `qwen3-next-80b-a3b-instruct`
  - `qwen3-vl-30b-a3b-thinking` / `qwen3-vl-30b-a3b-instruct`（多模态，支持图片输入）
  - `qwen3-vl-32b-thinking` / `qwen3-vl-32b-instruct`（多模态）
  - `qwen3-vl-235b-a22b-thinking` / `qwen3-vl-235b-a22b-instruct`（多模态，人工验证通过）
- **删除已整合的模型类**（功能已由 `VllmQwen3NextChatClient` 或基类统一覆盖）：
  - `VllmQwen2507ChatClient`（qwen3-235b-a22b-instruct-2507）— 已删除
  - `VllmQwen2507ReasoningChatClient`（qwen3-235b-a22b-thinking-2507）— 已删除
  - 对应测试 `Qwen2507ChatTests.cs`、`Qwen2507ReasoningChatTests.cs`、`Qwen3coderNextTests.cs` 同步删除
- 删除 `VllmChatClientNuget.Test` 测试项目（已不再需要）。

### 🧩 基类重构与适配器增强

- **`VllmBaseChatClient` 基类增强**：提取公共逻辑（请求构建、流式解析、推理内容处理）到基类，子类只需重写特定差异部分。
- **`VllmDeepseekR1ChatClient` 重构**：继承 `VllmBaseChatClient`，精简代码，仅保留 DeepSeek R1 特有的 `ReasoningContent` 流式处理逻辑。
- **`VllmGptOssChatClient` 重构**：继承 `VllmBaseChatClient`，精简大量重复代码，增强推理流式处理。

### 🛠️ 本地 Skill 自动加载

- 新增 `VllmChatOptions` 的 skill 自动加载功能：默认从运行目录 `./skills/*.md` 读取本地 skills，并自动注入系统提示词。
- 可通过 `EnableSkills`（默认 `true`）/ `SkillDirectoryPath` 控制开关与路径。
- 内置工具 `ListSkillFiles` 和 `ReadSkillFile`，模型可在对话中按需查询和读取 skill 文件。
- 新增 `SimpleSkillSmokeTests` 测试类验证 skill 功能。

### 📝 其他更新

- 新增 **Qwen 3.5** 支持（`qwen3.5-397b-a17b`），通过 `VllmQwen3NextChatClient` 接入。
- 新增 **MiniMax-M2.5** 支持，`VllmMiniMaxChatClient` 兼容 M2.5 / M2.1。
- 新增 **GLM 4.7 Flash** 支持。
- 新增 GLM 4.6/4.7/5 思维链支持：`VllmGlmChatClient`，支持推理分段流式输出（思考/答案）与函数调用。
- 新增 `GlmChatOptions`：通过 `ThinkingEnabled` 开关控制是否在请求体中发送智普官方平台所需的 `thinking: { type: "enabled" }`（默认关闭）。
- 新增 `KimiChatOptions`：通过 `ThinkingEnabled` 开关控制 Moonshot/Kimi 2.5 所需的 `thinking: { type: "enabled" | "disabled" }`。
- 修复/完善 `VllmKimiK2ChatClient` 思维链解析。
- 新增标签提取示例（基于 JSON 解析与正则匹配）。
- 新增 Gemini 3 支持（`VllmGemini3ChatClient`），详见 `docs/Gemini3*` 系列文档。
- Gemini 3 兼容双提供商：同一个 `VllmGemini3ChatClient` 可同时适配 **Google 原生 API** 与 **OpenRouter**（自动按 endpoint 切换认证头）。
- OpenRouter 兼容增强：请求体映射 `reasoning.enabled`，并修复工具回传消息字段（`tool_call_id` / `tool_calls`）以支持多轮函数调用。
- OpenRouter 的 `thoughtSignature` 在部分模型/响应中可能缺失，测试已调整为“有则校验、无则跳过严格断言”。

---

## 🔥 Latest Updates

### 🆕 Claude 4.6 / 4.5 Thinking Chain Support

- **`VllmClaudeChatClient` added**: Specifically designed for Claude models via platforms like OpenRouter.
- **Thinking Parameter Adaptation**: Supports the new `reasoning: { effort: "high" }` format introduced in Claude 4.6.
- **Reasoning Extraction**: Efficiently extracts reasoning content from both `reasoning` (string) and `reasoning_details` (array) response fields.
- **Token Optimization**: Includes default `MaxTokens` limits to prevent credit-related errors on cloud providers.

### 🆕 OpenAI GPT Series Support

- **`VllmOpenAiGptClient` added**: Specifically designed for OpenAI official or OpenRouter GPT models.
- **Reasoning Level Control**: Fine-tune model reasoning depth via `OpenAiGptChatOptions.ReasoningLevel`.
- **Reasoning Toggle**: Use `ExcludeReasoning` to easily include or omit the thinking process from the output.

### 🆕 DeepSeek V3.2 Thinking Chain Support

- **`VllmDeepseekV3ChatClient` thinking chain fixed**:
  - Corrected request format: DashScope API uses `enable_thinking: true` (top-level boolean) instead of `thinking: {type: "enabled"}`.
  - `reasoning_content` field in model responses is now correctly parsed and output.
  - Non-streaming: access thinking via `ReasoningChatResponse.Reason`.
  - Streaming: use `ReasoningChatResponseUpdate.Thinking` to distinguish thinking vs final answer.
  - Enable via `VllmChatOptions.ThinkingEnabled = true`.
  - Compatible with DashScope platform `deepseek-v3.2` model.

### 🐛 Bug Fixes

- **`VllmGptOssChatClient` Streaming Function Call Bug Fixed**:
  - Fixed an issue where the stream ended after model returned `tool_calls`, leaving the final text response empty.
  - Added `GetStreamingResponseAsync` override: automatically detects when the caller has appended tool results to `messages` and initiates a follow-up streaming request seamlessly.
  - `StreamChatManualFunctionCallTest` now works in a single `await foreach` loop without needing manual "Second turn" logic.
  - Simplified the default system prompt by removing the strict "content must be empty when tool_calls present" constraint.

### 🆕 GLM 4.6 / 4.7 / 5 Thinking Model Support
- **VllmGlmChatClient** added with full reasoning (thinking) stream separation.
- Supports `glm-5`, `glm-4.7`, `glm-4.7-flash`, `glm-4.6`, `glm-4.5`.

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

### 🔄 Base Class Refactoring & Model Consolidation
- **`VllmBaseChatClient`** enhanced: common logic (request building, streaming parsing, reasoning content handling) extracted to base class; subclasses only override specific differences.
- **`VllmDeepseekR1ChatClient`** refactored: inherits `VllmBaseChatClient`, retains only DeepSeek R1-specific `ReasoningContent` streaming logic.
- **`VllmGptOssChatClient`** refactored: inherits `VllmBaseChatClient`, significantly reduced duplicate code, enhanced reasoning streaming.
- **Removed** `VllmQwen2507ChatClient` and `VllmQwen2507ReasoningChatClient` (consolidated into `VllmQwen3NextChatClient`).
- **Removed** `VllmChatClientNuget.Test` project.

### 🛠️ Local Skill Auto-Loading
- `VllmChatOptions` now supports automatic skill loading from `./skills/*.md` files, injected into system prompts.
- Controlled via `EnableSkills` (default `true`) / `SkillDirectoryPath`.
- Built-in tools `ListSkillFiles` and `ReadSkillFile` allow models to query and read skill files during conversation.

### 🆕 Qwen3-Next / Qwen 3.5 Multi-Model Adaptation
- **VllmQwen3NextChatClient** now supports multiple model families via `modelId`:
  - `qwen3.5-397b-a17b` (Qwen 3.5, latest)
  - `qwen3-next-80b-a3b-thinking` / `qwen3-next-80b-a3b-instruct`
  - `qwen3-vl-30b-a3b-thinking` / `qwen3-vl-30b-a3b-instruct` (multimodal, image input)
  - `qwen3-vl-32b-thinking` / `qwen3-vl-32b-instruct` (multimodal)
  - `qwen3-vl-235b-a22b-thinking` / `qwen3-vl-235b-a22b-instruct` (multimodal, manually verified)
- Unified API: switch model by passing the desired modelId in constructor or per-request via `ChatOptions.ModelId`.
- Thinking models expose `ReasoningChatResponse` / streaming `ReasoningChatResponseUpdate`; instruct models output standard responses.
- New examples: Serial/Parallel tool calls, manual tool orchestration in streaming, JSON-only output formatting.

### 🆕 Kimi K2 Support
- **VllmKimiK2ChatClient** added.
- Supports Kimi models including `kimi-k2-thinking` and `kimi-k2.5`.
- Seamless reasoning streaming via `ReasoningChatResponseUpdate` (thinking vs final answer segments).
- Full function invocation support (automatic or manual tool call handling).

### 🆕 Kimi 2.5 Thinking Toggle (Moonshot)
- New `KimiChatOptions.ThinkingEnabled` to control request payload:
  - `ThinkingEnabled = true` -> `thinking: { "type": "enabled" }`
  - `ThinkingEnabled = false` -> `thinking: { "type": "disabled" }`
- Kimi reasoning text is taken from `reasoningContent` / streaming `delta.reasoning_content` (not `</think>` markers).

### 🆕 Gemini 3 Support & Tool Calling
- **VllmGemini3ChatClient** added (Google Gemini API)。
- Features: text & streaming, ReasoningLevel (Normal/Low), full tool calling (single / parallel / automatic / streaming)。
- Tests: `Gemini3Test` 全部通过（含多轮与并行工具调用）、`GeminiDebugTest` 覆盖原生 API 思维签名与多轮函数调用调试。
- Docs: 详见 `docs/Gemini3*` 文档合集。

### 🆕 Gemini 3 OpenRouter Compatibility
- `VllmGemini3ChatClient` now supports both **Google native Gemini API** and **OpenRouter** in one client.
- Auth header auto-switch by endpoint:
  - Google native: `x-goog-api-key`
  - OpenRouter/OpenAI-compatible: `Authorization: Bearer ...`
- OpenRouter reasoning mapping: sends top-level `reasoning.enabled` to match provider requirements.
- Tool-calling protocol compatibility: fixed `tool_call_id` / `tool_calls` request field names, and improved multi-turn tool-result roundtrip compatibility.
- In OpenRouter tests, `thoughtSignature` may be absent depending on model/provider behavior; assertions are now provider-tolerant.

### 🆕 MiniMax-M2.5 Support
- **VllmMiniMaxChatClient** added for MiniMax-M2.5 / M2.1 model support.
- Full streaming chat and function calling (parallel tool calls supported).
- Compatible with DashScope API endpoint.
- Tests: `MiniMaxTests` covering chat, streaming, function calls (serial/parallel/manual), and JSON output.

### 🆕 Qwen 3.5 Support
- **VllmQwen3NextChatClient** now supports Qwen 3.5 (`qwen3.5-397b-a17b`) via DashScope API.
- Full reasoning chain and function calling support.
- Use the same `VllmQwen3NextChatClient` with `modelId = "qwen3.5-397b-a17b"`.

---

## 🏗️ Supported Clients

| Client | Deployment | Model Support | Reasoning | Function Calls |
|--------|------------|---------------|-----------|----------------|
| `VllmOpenAiGptClient` | OpenRouter/Cloud | OpenAI GPT Series | ✅ Full | ✅ Stream |
| `VllmClaudeChatClient` | OpenRouter/Cloud | Claude 4.6 / 4.5 | ✅ Full | ✅ Stream |
| `VllmGptOssChatClient` | OpenRouter/Cloud | GPT-OSS-120B/20B | ✅ Full | ✅ Stream |
| `VllmQwen3ChatClient` | Local vLLM | Qwen3-32B/235B | ✅ Toggle | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | Multiple modelIds (e.g. qwen3-next-80b-a3b-thinking / qwen3-next-80b-a3b-instruct) | ✅ (thinking model) | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | qwen3-vl-30b-a3b-thinking / qwen3-vl-30b-a3b-instruct | ✅ (thinking model) | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | qwen3-vl-32b-thinking / qwen3-vl-32b-instruct | ✅ (thinking model) | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | qwen3-vl-235b-a22b-thinking / qwen3-vl-235b-a22b-instruct (manual verified) | ✅ (thinking model) | ✅ Stream |
| `VllmQwqChatClient` | Local vLLM | QwQ-32B | ✅ Full | ✅ Stream |
| `VllmGemmaChatClient` | Local vLLM | Gemma3-27B | ❌ | ✅ Stream |
| `VllmGemini3ChatClient` | Cloud API (Google Gemini / OpenRouter) | gemini-3-pro-preview / google/gemini-3.1-* | Signature (hidden, provider-dependent) | ✅ Stream |
| `VllmDeepseekR1ChatClient` | Cloud API | DeepSeek-R1 | ✅ Full | ❌ |
| `VllmDeepseekV3ChatClient` | Cloud API (DashScope) | DeepSeek-V3.2 | ✅ (via `VllmChatOptions`) | ✅ Stream |
| `VllmGlmChatClient` | Cloud API (Zhipu official) / OpenAI compatible | glm-5 / glm-4.6 / glm-4.7 / glm-4.7-flash / glm-4.5 | ✅ Full (via `GlmChatOptions`) | ✅ Stream |
| `VllmKimiK2ChatClient` | Cloud API (DashScope) | kimi-k2-(thinking/instruct) / kimi-k2.5 | ✅ (thinking model) | ✅ Stream |
| `VllmMiniMaxChatClient` | Cloud API (DashScope) | MiniMax-M2.5 / M2.1 | ✅ | ✅ Stream |
| `VllmQwen3NextChatClient` | Cloud API (DashScope compatible) | qwen3.5-397b-a17b | ✅ (thinking model) | ✅ Stream |

> 注：Gemini 3 的推理采用加密的 thought signature，不输出可读推理文本；OpenRouter 场景下 thoughtSignature 可能缺失，函数调用在当前实现中无需显式回传签名亦可完成多轮调用。

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

### 🆕 GLM 4.6/4.7/4.7-Flash Thinking Example


```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Glm4;

IChatClient glm46 = new VllmGlmChatClient(
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

### 🆕 Claude 4.6 / 4.5 with Reasoning (OpenRouter)

```csharp
using Microsoft.Extensions.AI;

// Initialize Claude client (OpenRouter)
IChatClient claude = new VllmClaudeChatClient(
    "https://openrouter.ai/api/v1",
    "your-api-key",
    "anthropic/claude-4.6-sonnet");

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个拥有强大逻辑推理能力的智能助手。"),
    new(ChatRole.User, "解释一下为什么天空是蓝色的？请详细思考。")
};

// Enable high-effort reasoning
var options = new VllmChatOptions { ThinkingEnabled = true };

// Non-streaming example:
var response = await claude.GetResponseAsync(messages, options);
if (response is ReasoningChatResponse r)
{
    Console.WriteLine($"🧠 Thinking:\n{r.Reason}");
    Console.WriteLine($"💬 Answer:\n{r.Text}");
}

// Streaming example:
await foreach (var update in claude.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate ru)
    {
        if (ru.Thinking)
            Console.Write(ru.Text); // Reasoning phase
        else
            Console.Write(ru.Text); // Answer phase
    }
}
```

### 🆕 OpenAI GPT Series with Reasoning (OpenRouter)

```csharp
using Microsoft.Extensions.AI;

// Initialize OpenAI GPT client (OpenRouter)
IChatClient gptClient = new VllmOpenAiGptClient(
    "https://openrouter.ai/api/v1",
    "your-api-key",
    "openai/gpt-5.2-codex");

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "You are a coding expert."),
    new(ChatRole.User, "Write a complex regex for email validation and explain it.")
};

// Set reasoning level and other options
var options = new OpenAiGptChatOptions 
{ 
    ReasoningLevel = OpenAiGptReasoningLevel.High,
    Temperature = 0.5f 
};

// Streaming with reasoning
await foreach (var update in gptClient.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate r)
    {
        if (r.Thinking)
            Console.Write(r.Text); // Reasoning phase
        else
            Console.Write(r.Text); // Answer phase
    }
}
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

### 🆕 DeepSeek-V3.2 with Thinking Chain

```csharp
using Microsoft.Extensions.AI;

// Initialize DeepSeek V3.2 client (DashScope API)
IChatClient dsV3 = new VllmDeepseekV3ChatClient(
    "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}",
    "your-api-key",
    "deepseek-v3.2");

var messages = new List<ChatMessage>
{
    new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
    new(ChatRole.User, "请解释一下相对论。")
};

// Enable thinking chain via VllmChatOptions
var options = new VllmChatOptions { ThinkingEnabled = true };

// Non-streaming: access reasoning via ReasoningChatResponse.Reason
var response = await dsV3.GetResponseAsync(messages, options);
if (response is ReasoningChatResponse reasoningResponse)
{
    Console.WriteLine($"🧠 Thinking: {reasoningResponse.Reason}");
    Console.WriteLine($"💬 Answer: {reasoningResponse.Text}");
}

// Streaming: distinguish thinking vs answer phases
string thinking = string.Empty;
string answer = string.Empty;
await foreach (var update in dsV3.GetStreamingResponseAsync(messages, options))
{
    if (update is ReasoningChatResponseUpdate r)
    {
        if (r.Thinking)
            thinking += r.Text;  // reasoning phase
        else
            answer += r.Text;    // final answer phase
    }
    else
    {
        answer += update.Text;
    }
}
Console.WriteLine($"🧠 Thinking: {thinking}");
Console.WriteLine($"💬 Answer: {answer}");
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
