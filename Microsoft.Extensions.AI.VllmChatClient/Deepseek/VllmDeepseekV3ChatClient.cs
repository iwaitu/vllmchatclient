using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI
{
    public class VllmDeepseekV3ChatClient : VllmBaseChatClient
    {
        public VllmDeepseekV3ChatClient(string endpoint, string? token = null, string? modelId = "kimi-k2-thinking", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 支持 VllmChatOptions 的思维链开关（DashScope API 使用 enable_thinking 布尔值）
            if (options is VllmChatOptions vllmOptions)
            {
                request.EnableThinking = vllmOptions.ThinkingEnabled;
            }

            return request;
        }
    }
}
