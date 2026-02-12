using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.Extensions.AI.VllmChatClient.Glm4
{
    public class VllmGlmChatClient : VllmBaseChatClient
    {
        public VllmGlmChatClient(string endpoint, string? token = null, string? modelId = "glm-4", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 支持 VllmChatOptions 或继承类（如 GlmChatOptions）的思维链控制
            if (options is VllmChatOptions vllmOptions)
            {
                request.Thinking = new VllmThinkingOptions
                {
                    Type = vllmOptions.ThinkingEnabled ? "enabled" : "disabled"
                };
            }

            return request;
        }
    }
}
