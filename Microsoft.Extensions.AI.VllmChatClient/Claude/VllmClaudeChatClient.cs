using Microsoft.Shared.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace Microsoft.Extensions.AI
{
    public class VllmClaudeChatClient : VllmBaseChatClient
    {
        public VllmClaudeChatClient(string endpoint, string? token = null, string? modelId = "anthropic/claude-opus-4.6", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 支持 VllmChatOptions 的思维链开关（OpenRouter Claude API 使用 reasoning: {effort: "high"}）
            if (options is VllmChatOptions vllmOptions && vllmOptions.ThinkingEnabled)
            {
                request.Reasoning = new VllmReasoningOptions
                {
                    Effort = "high"
                };
            }

            // Claude 默认 max_tokens 很大，OpenRouter 可能报错。如果没有设置，给予一个合理的默认值。
            if (options?.MaxOutputTokens == null)
            {
                request.MaxTokens = 8192;
            }

            return request;
        }

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(messages);

            string apiEndpoint = GetChatEndpoint();

            using var httpResponse = await HttpClient.PostAsJsonAsync(
                apiEndpoint,
                ToVllmChatRequest(messages, options, stream: false),
                JsonContext.Default.VllmOpenAIChatRequest,
                cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            var response = (await httpResponse.Content.ReadFromJsonAsync(
                JsonContext.Default.VllmChatResponse,
                cancellationToken).ConfigureAwait(false))!;

            if (response.Choices is null || response.Choices.Length == 0)
            {
                throw new InvalidOperationException("未返回任何响应选项。");
            }

            var responseMessage = response.Choices.FirstOrDefault()?.Message;
            
            // 优先提取 Claude 的 reasoning
            string reason = responseMessage?.Reasoning ?? string.Empty;
            if (string.IsNullOrEmpty(reason) && responseMessage?.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail)
            {
                reason += detail.Text;
            }
            
            // 回退到 ReasoningContent (兼容其他模型)
            if (string.IsNullOrEmpty(reason))
            {
                reason = responseMessage?.ReasoningContent?.ToString() ?? string.Empty;
            }

            var retMessage = FromVllmMessage(responseMessage!, options);
            bool hasToolCall = retMessage.Contents.Any(c => c is FunctionCallContent);

            return new ReasoningChatResponse(retMessage, reason)
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = hasToolCall ? ChatFinishReason.ToolCalls : ToFinishReason(response.Choices[0].FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? Metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseClaudeUsage(response),
            };
        }

        internal override ChatResponseUpdate? HandleStreamingReasoningContent(Delta delta, string responseId, string modelId)
        {
            // 优先检查 Claude 的 Reasoning
            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                return BuildTextUpdate(responseId, delta.Reasoning, true);
            }

            if (delta.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail && !string.IsNullOrEmpty(detail.Text))
            {
                return BuildTextUpdate(responseId, detail.Text, true);
            }

            // 回退到 ReasoningContent
            if (delta.ReasoningContent != null)
            {
                return BuildTextUpdate(responseId, delta.ReasoningContent, true);
            }

            return null;
        }

        private static UsageDetails? ParseClaudeUsage(VllmChatResponse response)
        {
            if (response?.Usage == null)
            {
                return null;
            }

            return new UsageDetails
            {
                InputTokenCount = response.Usage.PromptTokens,
                OutputTokenCount = response.Usage.CompletionTokens,
                TotalTokenCount = response.Usage.TotalTokens
            };
        }
    }
}
