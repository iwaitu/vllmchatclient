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

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messagesList = messages as IList<ChatMessage>;

            bool continueLoop;
            do
            {
                continueLoop = false;
                bool hasToolCalls = false;
                int messageCountBefore = messagesList?.Count ?? -1;

                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    if (update.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        hasToolCalls = true;
                    }
                    yield return update;
                }

                if (hasToolCalls &&
                    messagesList is not null &&
                    messagesList.Count > messageCountBefore &&
                    messagesList[messagesList.Count - 1].Role == ChatRole.Tool)
                {
                    continueLoop = true;
                }
            } while (continueLoop);
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 支持 VllmChatOptions 及其派生类（包括 KimiChatOptions）的思维链开关
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
