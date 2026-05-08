using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI
{
    public class VllmNemotronChatClient : VllmBaseChatClient
    {
        public VllmNemotronChatClient(string endpoint, string? token = null, string? modelId = "nvidia/nemotron-3-super-120b-a12b:free", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
           : base(NormalizeOpenAICompatibleEndpoint(endpoint, apiMode), token, modelId, httpClient, apiMode)
        {
        }

        private protected override void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            base.ApplyRequestOptions(request, options);

            if (options is VllmChatOptions vllmOptions)
            {
                request.Reasoning = new VllmReasoningOptions
                {
                    Enabled = vllmOptions.ThinkingEnabled
                };
            }
        }
    }
}
