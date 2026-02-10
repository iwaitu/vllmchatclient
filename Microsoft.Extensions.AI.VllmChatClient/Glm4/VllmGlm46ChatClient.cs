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
            
            // 支持 VllmChatOptions 及其派生类（包括 GlmChatOptions）的思维链开关
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
