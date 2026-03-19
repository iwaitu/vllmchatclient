using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI.VllmChatClient.Mimo
{
    public class VllmMimoChatClient : VllmBaseChatClient
    {
        public VllmMimoChatClient(string endpoint, string? token = null, string? modelId = "mimo-v2-pro", HttpClient? httpClient = null)
            : base(ProcessEndpoint(endpoint), null, modelId, httpClient)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                HttpClient.DefaultRequestHeaders.Authorization = null;
                HttpClient.DefaultRequestHeaders.Remove("api-key");
                HttpClient.DefaultRequestHeaders.Add("api-key", token);
            }
        }

        private static string ProcessEndpoint(string endpoint)
        {
            _ = Throw.IfNull(endpoint);

            endpoint = endpoint.Trim();
            if (endpoint.EndsWith("/", StringComparison.Ordinal))
            {
                endpoint = endpoint.TrimEnd('/');
            }

            if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            endpoint = endpoint
                .Replace("{0}", "v1", StringComparison.Ordinal)
                .Replace("{1}", string.Empty, StringComparison.Ordinal)
                .TrimEnd('/');

            if (endpoint.Contains("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/chat/completions";
            }

            return endpoint + "/v1/chat/completions";
        }

        private protected override void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            base.ApplyRequestOptions(request, options);

            if (options is VllmChatOptions vllmOptions)
            {
                request.ExtraBody ??= new Dictionary<string, object?>();
                request.ExtraBody["thinking"] = new Dictionary<string, object?>
                {
                    ["type"] = vllmOptions.ThinkingEnabled ? "enabled" : "disabled"
                };
            }

            if (request.MaxTokens is int maxCompletionTokens)
            {
                request.MaxCompletionTokens = maxCompletionTokens;
                request.MaxTokens = null;
            }

            if (request.Options?.temperature is float temperature)
            {
                request.Temperature = temperature;
                request.Options.temperature = null;
            }

            if (request.Options?.top_p is float topP)
            {
                request.TopP = topP;
                request.Options.top_p = null;
            }

            if (request.Options is { temperature: null, top_p: null, extra_body: null })
            {
                request.Options = null;
            }
        }

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

            if (ShouldExposeReasoning(options) || response is not ReasoningChatResponse reasoningResponse || response.Messages.Count == 0)
            {
                return response;
            }

            return new ReasoningChatResponse(response.Messages[0], string.Empty)
            {
                CreatedAt = reasoningResponse.CreatedAt,
                FinishReason = reasoningResponse.FinishReason,
                ModelId = reasoningResponse.ModelId,
                ResponseId = reasoningResponse.ResponseId,
                Usage = reasoningResponse.Usage,
            };
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                if (!ShouldExposeReasoning(options) && update is ReasoningChatResponseUpdate { Thinking: true })
                {
                    continue;
                }

                yield return update;
            }
        }

        private static bool ShouldExposeReasoning(ChatOptions? options)
            => options is not VllmChatOptions vllmOptions || vllmOptions.ThinkingEnabled;
    }
}
