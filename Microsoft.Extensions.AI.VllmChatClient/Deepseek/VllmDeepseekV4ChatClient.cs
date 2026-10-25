// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

            // 历史消息中回传给模型的推理文本不应再带有审核标记
            foreach (var message in request.Messages)
            {
                if (message.ReasoningContent is string reasoning && DeepseekSafetyTagFilter.MayContainTag(reasoning))
                {
                    message.ReasoningContent = DeepseekSafetyTagFilter.Strip(reasoning);
                }
            }

            return request;
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
