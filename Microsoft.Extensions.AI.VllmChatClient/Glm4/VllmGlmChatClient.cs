namespace Microsoft.Extensions.AI
{
    public class VllmGlmChatClient : VllmBaseChatClient
    {
        private const string ZhipuPrefix = "zhipu/";

        public VllmGlmChatClient(string endpoint, string? token = null, string? modelId = "glm-4", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(endpoint, token, modelId, httpClient, apiMode)
        {
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            // 支持 VllmChatOptions 或继承类（如 GlmChatOptions）的思维链控制
            if (options is VllmChatOptions vllmOptions)
            {
                if (IsAlwaysThinkingModel(request.Model))
                {
                    ApplyAlwaysThinking(request, vllmOptions);
                }
                else
                {
                    request.Thinking = new VllmThinkingOptions
                    {
                        Type = vllmOptions.ThinkingEnabled ? "enabled" : "disabled"
                    };
                }
            }

            return request;
        }

        /// <summary>
        /// GLM-5.3-Flash 始终思考，服务端对 thinking disabled / enable_thinking=false 返回 400（code 1210），只接受 low/high/max。
        /// 关闭思考 → 最低档 low（不论 ReasoningEffort 是什么）；启用思考 → ReasoningEffort 为合法档位（low/high/max）时显式传递；null/空白视为未设置，使用模型默认；非空且不合法则抛 ArgumentException（发请求前）。
        /// 只修改本次新建的 request，不触碰调用方的 options。
        /// </summary>
        private static void ApplyAlwaysThinking(VllmOpenAIChatRequest request, VllmChatOptions options)
        {
            request.Thinking = new VllmThinkingOptions { Type = "enabled" };
            request.EnableThinking = null;
            request.Reasoning = null;
            request.ChatTemplateKwargs?.Remove("enable_thinking");
            request.ExtraBody?.Remove("enable_thinking");
            request.ExtraBody?.Remove("thinking");
            request.ExtraBody?.Remove("reasoning_effort");

            request.ReasoningEffort = options.ThinkingEnabled
                ? NormalizeEffort(options.ReasoningEffort)
                : "low";
        }

        // null/空白 = 未设置（沿用模型默认）；非空但不支持的档位在发请求前抛错，避免静默回落到可能昂贵的默认档。
        private static string? NormalizeEffort(string? effort)
        {
            if (string.IsNullOrWhiteSpace(effort))
            {
                return null;
            }

            var value = effort.Trim().ToLowerInvariant();
            if (value is "low" or "high" or "max")
            {
                return value;
            }

            throw new ArgumentException(
                $"Unsupported ReasoningEffort '{effort}' for GLM-5.3-Flash; supported values are low, high, and max. No request was sent.",
                nameof(VllmChatOptions.ReasoningEffort));
        }

        /// <summary>精确匹配（忽略大小写及可选的 ZHIPU/ 前缀）；不使用 StartsWith，避免误伤 FlashX 等未知型号。</summary>
        private static bool IsAlwaysThinkingModel(string? modelId)
        {
            var id = modelId?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (id.StartsWith(ZhipuPrefix, StringComparison.Ordinal))
            {
                id = id.Substring(ZhipuPrefix.Length);
            }

            return id == "glm-5.3-flash";
        }
    }
}
