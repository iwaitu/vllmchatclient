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
    }
}
