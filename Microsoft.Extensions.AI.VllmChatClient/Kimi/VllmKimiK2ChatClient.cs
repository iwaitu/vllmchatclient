using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.Extensions.AI.VllmChatClient.Kimi
{
    public class VllmKimiK2ChatClient : VllmBaseChatClient
    {
        public VllmKimiK2ChatClient(string endpoint, string? token = null, string? modelId = "kimi-k2-thinking", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);
            if (options is KimiChatOptions kimiOptions)
            {
                request.Thinking = new VllmThinkingOptions { Type = kimiOptions.ThinkingEnabled ? "enabled" : "disabled" };
            }

            return request;
        }
    }
}
