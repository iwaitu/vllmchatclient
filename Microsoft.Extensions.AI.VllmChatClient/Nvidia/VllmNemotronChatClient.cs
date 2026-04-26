using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI
{
    public class VllmNemotronChatClient : VllmBaseChatClient
    {
        public VllmNemotronChatClient(string endpoint, string? token = null, string? modelId = "nvidia/nemotron-3-super-120b-a12b:free", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
           : base(ProcessEndpoint(endpoint), token, modelId, httpClient, apiMode)
        {
        }

        private static string ProcessEndpoint(string endpoint)
        {
            _ = Throw.IfNull(endpoint);

            if (endpoint.EndsWith("/", StringComparison.Ordinal))
            {
                endpoint = endpoint.TrimEnd('/');
            }

            if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.Contains("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/chat/completions";
            }

            return endpoint + "/{0}/{1}";
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
