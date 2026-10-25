// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// DeepSeek V4.x 系列（deepseek-v4.1-flash、deepseek-v4.1-flash-1017 等）的 ChatClient。
    /// 在 <see cref="VllmDeepseekV3ChatClient"/> 的基础上，剔除部分快照泄漏到推理/回答文本中的
    /// &lt;ds_safety&gt;…&lt;/ds_safety&gt; 审核块（流式下该标签会被拆在多个 chunk 中）。
    /// </summary>
    public class VllmDeepseekV4ChatClient : VllmDeepseekV3ChatClient
    {
        public VllmDeepseekV4ChatClient(string endpoint, string? token = null, string? modelId = "deepseek-v4.1-flash", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(endpoint, token, modelId, httpClient, apiMode)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 历史消息中回传给模型的推理文本不应再带有审核标记。
            // ReasoningContent 是 object?：流式累计的值是 CLR string，非流式原始响应反序列化的值是 JsonElement(String)，
            // 两者都按字符串处理；其它 JSON 类型（数组/对象）原样保留，不转成文本。只改这条出站请求的副本。
            foreach (var message in request.Messages)
            {
                var reasoning = message.ReasoningContent switch
                {
                    string text => text,
                    JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                    _ => null,
                };

                if (reasoning is not null && DeepseekSafetyTagFilter.MayContainTag(reasoning))
                {
                    message.ReasoningContent = DeepseekSafetyTagFilter.Strip(reasoning);
                }
            }

            return request;
        }

        private protected override VllmAnthropicMessagesRequest ToVllmAnthropicMessagesRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmAnthropicMessagesRequest(messages, options, stream);

            // 原生历史回放优先使用 AnthropicContentBlocks（来自原始响应块，未经显示过滤）。只在这份新建的请求 DTO 上，
            // 清理 assistant 消息里已知的 text 块与未签名 thinking 块；其它角色、其它块与调用方对象一律不动。
            foreach (var message in request.Messages)
            {
                if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) && message.Content.ValueKind == JsonValueKind.Array)
                {
                    message.Content = StripAssistantBlocks(message.Content);
                }
            }

            return request;
        }

        /// <summary>
        /// 当 <paramref name="block"/> 是含审核标签的 <c>text</c> 块，或不带签名的 <c>thinking</c> 块时，返回要改写的字段名和清理后的文本。
        /// 带非空 signature 的 thinking（不透明签名对象）、redacted_thinking、tool_use、tool_result 与未知块都不改。
        /// </summary>
        private static bool TryGetCleanedField(JsonElement block, out string field, out string cleaned)
        {
            field = string.Empty;
            cleaned = string.Empty;
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            switch (type.GetString())
            {
                case "text":
                    field = "text";
                    break;
                case "thinking":
                    if (block.TryGetProperty("signature", out var signature) &&
                        signature.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
                        !(signature.ValueKind == JsonValueKind.String && signature.GetString()!.Length == 0))
                    {
                        return false;
                    }

                    field = "thinking";
                    break;
                default:
                    return false;
            }

            if (!block.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var original = value.GetString()!;
            if (!DeepseekSafetyTagFilter.MayContainTag(original))
            {
                return false;
            }

            cleaned = DeepseekSafetyTagFilter.Strip(original);
            return true;
        }

        /// <summary>只在确有匹配的块上改写；没有任何匹配时原样返回同一个 JsonElement（出站字节与 2.0.29 完全相同）。</summary>
        private static JsonElement StripAssistantBlocks(JsonElement content)
        {
            var any = false;
            foreach (var block in content.EnumerateArray())
            {
                if (TryGetCleanedField(block, out _, out _))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return content;
            }

            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                foreach (var block in content.EnumerateArray())
                {
                    if (!TryGetCleanedField(block, out var field, out var cleaned))
                    {
                        block.WriteTo(writer);
                        continue;
                    }

                    // 按属性复制，只替换目标字符串；其它属性（含未知属性）的值与顺序保持不变。
                    writer.WriteStartObject();
                    foreach (var property in block.EnumerateObject())
                    {
                        if (property.NameEquals(field))
                        {
                            writer.WriteString(property.Name, cleaned);
                        }
                        else
                        {
                            property.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                foreach (var content in message.Contents)
                {
                    if (content is TextContent text && DeepseekSafetyTagFilter.MayContainTag(text.Text))
                    {
                        text.Text = DeepseekSafetyTagFilter.Strip(text.Text);
                    }
                }
            }

            if (response is ReasoningChatResponse reasoning &&
                DeepseekSafetyTagFilter.MayContainTag(reasoning.Reason) &&
                response.Messages.Count > 0)
            {
                return new ReasoningChatResponse(response.Messages[0], DeepseekSafetyTagFilter.Strip(reasoning.Reason))
                {
                    CreatedAt = response.CreatedAt,
                    FinishReason = response.FinishReason,
                    ModelId = response.ModelId,
                    ResponseId = response.ResponseId,
                    Usage = response.Usage,
                    RawRepresentation = response.RawRepresentation,
                    AdditionalProperties = response.AdditionalProperties,
                };
            }

            return response;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reasoningFilter = new DeepseekSafetyTagFilter();
            var answerFilter = new DeepseekSafetyTagFilter();
            ChatResponseUpdate? last = null;

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                var filter = update is ReasoningChatResponseUpdate { Thinking: true } ? reasoningFilter : answerFilter;
                last = update;

                bool hadText = false;
                for (int i = update.Contents.Count - 1; i >= 0; i--)
                {
                    if (update.Contents[i] is not TextContent text || string.IsNullOrEmpty(text.Text))
                    {
                        continue;
                    }

                    hadText = true;
                    text.Text = filter.Push(text.Text);
                    if (text.Text.Length == 0)
                    {
                        update.Contents.RemoveAt(i);
                    }
                }

                // chunk 仅由被剔除/暂存的文本构成时，不再向外发送空更新
                if (hadText && update.Contents.Count == 0 && update.FinishReason is null)
                {
                    continue;
                }

                yield return update;
            }

            // 流结束后吐出因疑似标签前缀而暂存的尾部文本
            foreach (var (filter, thinking) in new[] { (reasoningFilter, true), (answerFilter, false) })
            {
                string tail = filter.Flush();
                if (tail.Length == 0)
                {
                    continue;
                }

                yield return new ReasoningChatResponseUpdate
                {
                    CreatedAt = DateTimeOffset.Now,
                    ModelId = last?.ModelId,
                    ResponseId = last?.ResponseId,
                    Role = ChatRole.Assistant,
                    Thinking = thinking,
                    Contents = [new TextContent(tail)],
                };
            }
        }
    }
}
