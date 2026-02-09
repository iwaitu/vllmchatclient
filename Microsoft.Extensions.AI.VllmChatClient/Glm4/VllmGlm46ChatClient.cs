using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.Extensions.AI.VllmChatClient.Glm4
{
    public class VllmGlm46ChatClient : VllmBaseChatClient
    {
        public VllmGlm46ChatClient(string endpoint, string? token = null, string? modelId = "glm-4.6", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);
            if (options is GlmChatOptions glmOptions)
            {
                request.Thinking = new VllmThinkingOptions
                {
                    Type = glmOptions.ThinkingEnabled ? "enabled" : "disabled"
                };
            }

            return request;
        }
    }
}
