using Microsoft.Extensions.AI;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace VllmChatClient.Test;

public class ClaudeRawHttpTests
{
    private readonly ITestOutputHelper _output;
    private readonly string? _apiKey;
    private readonly bool _skipTests;

    public ClaudeRawHttpTests(ITestOutputHelper output)
    {
        _output = output;
        _apiKey = Environment.GetEnvironmentVariable("OPEN_ROUTE_API_KEY");
        _skipTests = string.IsNullOrWhiteSpace(_apiKey);
    }

    /// <summary>
    /// 直接用 HttpClient 请求 OpenRouter，捕获原始 JSON 响应并逐字段验证反序列化结果。
    /// </summary>
    [Fact]
    public async Task RawHttpRequest_VerifyResponseFields()
    {
        if (_skipTests) return;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // 1. 构造请求 — 与 VllmClaudeChatClient 相同的逻辑
        var request = new VllmOpenAIChatRequest
        {
            Model = "anthropic/claude-opus-4.6",
            Messages =
            [
                new VllmOpenAIChatRequestMessage { Role = "system", Content = "你是菲菲。" },
                new VllmOpenAIChatRequestMessage { Role = "user",   Content = "你好" }
            ],
            Stream = false,
            Reasoning = new VllmReasoningOptions { Effort = "high" },
            MaxTokens = 1024
        };

        // 2. 输出序列化后的请求体
        var requestJson = JsonSerializer.Serialize(request, JsonContext.Default.VllmOpenAIChatRequest);
        _output.WriteLine("===== REQUEST JSON =====");
        _output.WriteLine(requestJson);

        // 3. 发送请求
        var httpResponse = await httpClient.PostAsJsonAsync(
            "https://openrouter.ai/api/v1/chat/completions",
            request,
            JsonContext.Default.VllmOpenAIChatRequest);

        // 4. 读取原始响应
        var rawResponse = await httpResponse.Content.ReadAsStringAsync();
        _output.WriteLine("===== RAW RESPONSE =====");
        _output.WriteLine(rawResponse);

        Assert.True(httpResponse.IsSuccessStatusCode, $"HTTP {httpResponse.StatusCode}: {rawResponse}");

        // 5. 用 JsonDocument 解析原始 JSON，逐字段检查
        using var doc = JsonDocument.Parse(rawResponse);
        var root = doc.RootElement;

        var choices = root.GetProperty("choices");
        Assert.True(choices.GetArrayLength() > 0, "choices 为空");

        var message = choices[0].GetProperty("message");
        _output.WriteLine("===== MESSAGE FIELDS =====");
        foreach (var prop in message.EnumerateObject())
        {
            var valueStr = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.GetRawText();
            // 截断过长的值
            if (valueStr?.Length > 200) valueStr = valueStr[..200] + "...";
            _output.WriteLine($"  {prop.Name}: {valueStr}");
        }

        // 6. 用 JsonContext 源生成器反序列化
        var response = JsonSerializer.Deserialize(rawResponse, JsonContext.Default.VllmChatResponse);
        Assert.NotNull(response);

        var msg = response!.Choices.FirstOrDefault()?.Message;
        Assert.NotNull(msg);

        _output.WriteLine("===== DESERIALIZED MESSAGE =====");
        _output.WriteLine($"  Content:          '{msg!.Content}'");
        _output.WriteLine($"  Reasoning:        '{msg.Reasoning}'");
        _output.WriteLine($"  ReasoningContent: '{msg.ReasoningContent}'");
        _output.WriteLine($"  ReasoningDetails: {(msg.ReasoningDetails == null ? "null" : $"count={msg.ReasoningDetails.Length}")}");
        if (msg.ReasoningDetails != null)
        {
            foreach (var d in msg.ReasoningDetails)
            {
                _output.WriteLine($"    type={d.Type}, text='{(d.Text.Length > 100 ? d.Text[..100] + "..." : d.Text)}'");
            }
        }
        _output.WriteLine($"  Refusal:          '{msg.Refusal}'");
        _output.WriteLine($"  ToolCalls:        {(msg.ToolCalls == null ? "null" : $"count={msg.ToolCalls.Length}")}");

        // 7. 验证原始 JSON 与反序列化结果一致
        bool rawHasReasoning = message.TryGetProperty("reasoning", out _);
        bool rawHasReasoningDetails = message.TryGetProperty("reasoning_details", out _);
        bool rawHasReasoningContent = message.TryGetProperty("reasoning_content", out _);

        _output.WriteLine("===== FIELD PRESENCE =====");
        _output.WriteLine($"  raw has 'reasoning':         {rawHasReasoning}");
        _output.WriteLine($"  raw has 'reasoning_details': {rawHasReasoningDetails}");
        _output.WriteLine($"  raw has 'reasoning_content': {rawHasReasoningContent}");
        _output.WriteLine($"  deserialized Reasoning:        is {(string.IsNullOrEmpty(msg.Reasoning) ? "EMPTY" : "SET")}");
        _output.WriteLine($"  deserialized ReasoningDetails:  is {(msg.ReasoningDetails == null || msg.ReasoningDetails.Length == 0 ? "EMPTY" : "SET")}");
        _output.WriteLine($"  deserialized ReasoningContent:  is {(msg.ReasoningContent == null ? "NULL" : "SET")}");

        // 如果原始 JSON 有字段但反序列化丢失，测试失败并输出差异
        if (rawHasReasoning && string.IsNullOrEmpty(msg.Reasoning))
        {
            var rawVal = message.GetProperty("reasoning").GetRawText();
            _output.WriteLine($"  [MISMATCH] raw reasoning = {rawVal[..Math.Min(200, rawVal.Length)]}");
        }
        if (rawHasReasoningDetails && (msg.ReasoningDetails == null || msg.ReasoningDetails.Length == 0))
        {
            var rawVal = message.GetProperty("reasoning_details").GetRawText();
            _output.WriteLine($"  [MISMATCH] raw reasoning_details = {rawVal[..Math.Min(200, rawVal.Length)]}");
        }

        // 最终断言：至少有一个推理来源应该有值
        bool hasAnyReasoning = !string.IsNullOrEmpty(msg.Reasoning)
                            || msg.ReasoningContent != null
                            || (msg.ReasoningDetails?.Length > 0);

        Assert.True(hasAnyReasoning,
            "所有推理字段均为空。请检查上方 RAW RESPONSE 和 FIELD PRESENCE 输出。");

        // 8. 关键诊断：模拟 VllmBaseChatClient.GetResponseAsync 的推理提取逻辑
        _output.WriteLine("===== REASONING EXTRACTION LOGIC =====");

        string reason = msg.ReasoningContent?.ToString() ?? string.Empty;
        _output.WriteLine($"  Step1: ReasoningContent?.ToString() => '{reason}' (length={reason.Length})");
        _output.WriteLine($"  Step1: ReasoningContent is null? {msg.ReasoningContent == null}");
        _output.WriteLine($"  Step1: ReasoningContent type: {msg.ReasoningContent?.GetType().FullName ?? "null"}");
        _output.WriteLine($"  Step1: string.IsNullOrEmpty(reason)? {string.IsNullOrEmpty(reason)}");

        if (string.IsNullOrEmpty(reason))
        {
            reason = msg.Reasoning ?? string.Empty;
            _output.WriteLine($"  Step2: Reasoning => '{(reason.Length > 100 ? reason[..100] + "..." : reason)}'");
        }
        else
        {
            _output.WriteLine($"  Step2: SKIPPED — reason already set from ReasoningContent!");
        }

        if (string.IsNullOrEmpty(reason) && msg.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } d2)
        {
            reason = d2.Text;
            _output.WriteLine($"  Step3: ReasoningDetails => '{(reason.Length > 100 ? reason[..100] + "..." : reason)}'");
        }

        _output.WriteLine($"  FINAL reason: '{(reason.Length > 100 ? reason[..100] + "..." : reason)}' (length={reason.Length})");
        Assert.False(string.IsNullOrEmpty(reason), "推理提取逻辑最终仍为空！");
    }

    /// <summary>
    /// 捕获 Claude streaming SSE 原始数据，查看推理字段的格式。
    /// </summary>
    [Fact]
    public async Task RawStreamingRequest_VerifyDeltaFields()
    {
        if (_skipTests) return;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var request = new VllmOpenAIChatRequest
        {
            Model = "anthropic/claude-opus-4.6",
            Messages =
            [
                new VllmOpenAIChatRequestMessage { Role = "system", Content = "你是菲菲。" },
                new VllmOpenAIChatRequestMessage { Role = "user",   Content = "你好" }
            ],
            Stream = true,
            Reasoning = new VllmReasoningOptions { Effort = "high" },
            MaxTokens = 1024
        };

        var requestJson = JsonSerializer.Serialize(request, JsonContext.Default.VllmOpenAIChatRequest);
        _output.WriteLine("===== STREAM REQUEST =====");
        _output.WriteLine(requestJson);

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = JsonContent.Create(request, JsonContext.Default.VllmOpenAIChatRequest)
        };
        using var httpResponse = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead);

        Assert.True(httpResponse.IsSuccessStatusCode, $"HTTP {httpResponse.StatusCode}");

        using var stream = await httpResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        _output.WriteLine("===== SSE EVENTS (first 30 with delta content) =====");
        int eventCount = 0;
        int shownCount = 0;

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                continue;

            var jsonPart = System.Text.RegularExpressions.Regex.Replace(line, @"^data:\s*", "");
            if (string.IsNullOrEmpty(jsonPart) || jsonPart == "[DONE]")
                continue;

            eventCount++;

            using var doc = JsonDocument.Parse(jsonPart);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var delta = choices[0].GetProperty("delta");

            // 输出每个 delta 中的所有字段
            if (shownCount < 30)
            {
                var fields = new List<string>();
                foreach (var prop in delta.EnumerateObject())
                {
                    var val = prop.Value.ValueKind == JsonValueKind.String
                        ? $"\"{prop.Value.GetString()}\""
                        : prop.Value.GetRawText();
                    if (val.Length > 80) val = val[..80] + "...";
                    fields.Add($"{prop.Name}={val}");
                }
                _output.WriteLine($"  event[{eventCount}] delta: {string.Join(", ", fields)}");
                shownCount++;
            }

            // 检查推理相关字段
            bool hasDeltaReasoning = delta.TryGetProperty("reasoning", out _);
            bool hasDeltaReasoningContent = delta.TryGetProperty("reasoning_content", out _);
            bool hasDeltaReasoningDetails = delta.TryGetProperty("reasoning_details", out _);

            if (hasDeltaReasoning || hasDeltaReasoningContent || hasDeltaReasoningDetails)
            {
                _output.WriteLine($"  >>> REASONING FOUND in event[{eventCount}]:");
                if (hasDeltaReasoning) _output.WriteLine($"      reasoning: {delta.GetProperty("reasoning").GetRawText()[..Math.Min(100, delta.GetProperty("reasoning").GetRawText().Length)]}");
                if (hasDeltaReasoningContent) _output.WriteLine($"      reasoning_content: {delta.GetProperty("reasoning_content").GetRawText()[..Math.Min(100, delta.GetProperty("reasoning_content").GetRawText().Length)]}");
                if (hasDeltaReasoningDetails) _output.WriteLine($"      reasoning_details: {delta.GetProperty("reasoning_details").GetRawText()[..Math.Min(100, delta.GetProperty("reasoning_details").GetRawText().Length)]}");
            }
        }

        _output.WriteLine($"===== Total SSE events: {eventCount} =====");

        // 关键测试：用 JsonContext 反序列化一个含 reasoning 的 delta
        var testChunkJson = "{\"id\":\"test\",\"model\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":0,\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning\":\"hello\",\"reasoning_details\":[{\"type\":\"reasoning.text\",\"text\":\"hello\",\"format\":\"anthropic-claude-v1\",\"index\":0}]}}]}";

        var testChunk = JsonSerializer.Deserialize(testChunkJson, JsonContext.Default.VllmChatStreamResponse);
        var testDelta = testChunk?.Choices?.FirstOrDefault()?.Delta;
        _output.WriteLine("===== DELTA DESERIALIZATION TEST =====");
        _output.WriteLine($"  delta is null: {testDelta == null}");
        _output.WriteLine($"  delta.Reasoning: '{testDelta?.Reasoning}'");
        _output.WriteLine($"  delta.ReasoningContent: '{testDelta?.ReasoningContent}'");
        _output.WriteLine($"  delta.ReasoningDetails: {(testDelta?.ReasoningDetails == null ? "null" : $"count={testDelta.ReasoningDetails.Length}")}");
        _output.WriteLine($"  delta.Content: '{testDelta?.Content}'");

        Assert.NotNull(testDelta);
        Assert.Equal("hello", testDelta!.Reasoning);
    }

    /// <summary>
    /// 用 VllmClaudeChatClient 的 GetStreamingResponseAsync，检查每个 update 的类型和内容。
    /// </summary>
    [Fact]
    public async Task StreamingClient_DebugUpdates()
    {
        if (_skipTests) return;

        var client = new VllmClaudeChatClient("https://openrouter.ai/api/{0}/{1}", _apiKey, "anthropic/claude-opus-4.6");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, "你是菲菲。"),
            new ChatMessage(ChatRole.User, "你好")
        };
        var chatOptions = new VllmChatOptions
        {
            ThinkingEnabled = true,
            MaxOutputTokens = 1024
        };

        int count = 0;
        string think = "";
        string response = "";
        await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
        {
            count++;
            var typeName = update.GetType().Name;
            var isReasoning = update is ReasoningChatResponseUpdate;
            var thinkingFlag = (update as ReasoningChatResponseUpdate)?.Thinking;
            var text = update.Text ?? "(null)";
            if (text.Length > 60) text = text[..60] + "...";

            if (count <= 20)
            {
                _output.WriteLine($"  [{count}] type={typeName}, isReasoning={isReasoning}, thinking={thinkingFlag}, text='{text}'");
            }

            if (update is ReasoningChatResponseUpdate ru)
            {
                if (ru.Thinking) think += update.Text;
                else response += update.Text;
            }
            else
            {
                response += update.Text;
            }
        }

        _output.WriteLine($"===== Total updates: {count} =====");
        _output.WriteLine($"  Thinking: '{(think.Length > 200 ? think[..200] + "..." : think)}'");
        _output.WriteLine($"  Response: '{(response.Length > 200 ? response[..200] + "..." : response)}'");

        Assert.False(string.IsNullOrEmpty(think), "Streaming thinking 为空！");
        Assert.False(string.IsNullOrEmpty(response), "Streaming response 为空！");
    }
}
