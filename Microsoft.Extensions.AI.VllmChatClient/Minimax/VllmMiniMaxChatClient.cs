namespace Microsoft.Extensions.AI
{

    public class VllmMiniMaxChatClient : VllmBaseChatClient
    {
        public VllmMiniMaxChatClient(string endpoint, string? token = null, string? modelId = "kimi-k2-thinking", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        
    }
}
