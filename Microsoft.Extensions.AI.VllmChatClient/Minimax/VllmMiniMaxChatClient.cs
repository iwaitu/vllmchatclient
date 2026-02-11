namespace Microsoft.Extensions.AI
{

    public class VllmMiniMaxChatClient : VllmBaseChatClient
    {
        public VllmMiniMaxChatClient(string endpoint, string? token = null, string? modelId = "MiniMax-M2.1", HttpClient? httpClient = null)
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
    }
}
