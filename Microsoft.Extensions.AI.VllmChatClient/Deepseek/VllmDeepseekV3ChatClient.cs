// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Shared.Diagnostics;
using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    public class VllmDeepseekV3ChatClient : VllmBaseChatClient
    {
        public VllmDeepseekV3ChatClient(string endpoint, string? token = null, string? modelId = "deepseek-v4-flash", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(ProcessEndpoint(endpoint, apiMode), token, modelId, httpClient, apiMode)
        {
        }

        private static string ProcessEndpoint(string endpoint, VllmApiMode apiMode)
        {
            _ = Throw.IfNull(endpoint);
            endpoint = endpoint.TrimEnd('/');

            if (endpoint.Contains("{0}", StringComparison.Ordinal) || endpoint.Contains("{1}", StringComparison.Ordinal))
            {
                return endpoint;
            }

            return apiMode switch
            {
                VllmApiMode.AnthropicMessages => ProcessAnthropicEndpoint(endpoint),
                VllmApiMode.Responses => ProcessResponsesEndpoint(endpoint),
                _ => ProcessChatCompletionsEndpoint(endpoint),
            };
        }

        private static string ProcessChatCompletionsEndpoint(string endpoint)
        {
            if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/chat/completions";
            }

            return endpoint + "/v1/chat/completions";
        }

        private static string ProcessAnthropicEndpoint(string endpoint)
        {
            if (endpoint.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/messages";
            }

            return endpoint + "/v1/messages";
        }

        private static string ProcessResponsesEndpoint(string endpoint)
        {
            if (endpoint.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/responses";
            }

            return endpoint + "/v1/responses";
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            if (options?.ResponseFormat is not null)
            {
                request.ResponseFormat = null;
            }

            // 支持 VllmChatOptions 的思维链开关（DashScope API 使用 enable_thinking 布尔值）
            if (options is VllmChatOptions vllmOptions)
            {
                request.EnableThinking = vllmOptions.ThinkingEnabled;
            }

            return request;
        }

        private protected override VllmAnthropicMessagesRequest ToVllmAnthropicMessagesRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmAnthropicMessagesRequest(messages, options, stream);

            foreach (var message in request.Messages)
            {
                if (message.Content.ValueKind == JsonValueKind.String)
                {
                    var text = message.Content.GetString();
                    message.Content = JsonSerializer.SerializeToElement(new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = text ?? string.Empty
                        }
                    });
                }
            }

            return request;
        }
    }
}
