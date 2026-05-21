namespace Microsoft.Extensions.AI
{
    public class VllmQwen3ChatClient : VllmBaseChatClient
    {
        private const string NoThinkDirective = "/no_think";
        private const string ThinkStartTag = "<think>";
        private const string ThinkEndTag = "</think>";
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ThinkStreamState> _thinkStreamStates = new();

        private sealed class ThinkStreamState
        {
            public bool InsideThink { get; set; }
        }

        public VllmQwen3ChatClient(
            string endpoint,
            string? token = null,
            string? modelId = "qwen3",
            HttpClient? httpClient = null,
            VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(NormalizeOpenAICompatibleEndpoint(endpoint, apiMode), token, modelId, httpClient, apiMode)
        {
        }

        protected override bool EnableLegacyToolCallTextFallback(ChatOptions? options) => true;

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var responseIds = new HashSet<string>();

            try
            {
                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(update.ResponseId))
                    {
                        responseIds.Add(update.ResponseId);
                    }

                    yield return update;
                }
            }
            finally
            {
                foreach (var responseId in responseIds)
                {
                    _thinkStreamStates.TryRemove(responseId, out _);
                }
            }
        }

        internal override ChatResponseUpdate? HandleStreamingReasoningContent(Delta delta, string responseId, string modelId)
        {
            var structuredReasoning = base.HandleStreamingReasoningContent(delta, responseId, modelId);
            if (structuredReasoning is not null)
            {
                return structuredReasoning;
            }

            if (string.IsNullOrEmpty(delta.Content))
            {
                return null;
            }

            var state = _thinkStreamStates.GetOrAdd(responseId, static _ => new ThinkStreamState());
            var content = delta.Content;
            var reasoning = ExtractThinkTaggedReasoning(ref content, state);
            delta.Content = string.IsNullOrEmpty(content) ? null : content;

            return string.IsNullOrEmpty(reasoning)
                ? null
                : BuildTextUpdate(responseId, reasoning, thinking: true);
        }

        private protected override IEnumerable<ChatMessage> PrepareMessagesWithSkills(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var preparedMessages = base.PrepareMessagesWithSkills(messages, options).ToList();

            if (options is VllmChatOptions { ThinkingEnabled: false })
            {
                InsertNoThinkDirective(preparedMessages);
            }

            return preparedMessages;
        }

        private protected override void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            base.ApplyRequestOptions(request, options);

            if (options?.TopK is int topK)
            {
                (request.Options ??= new()).top_k = topK;
            }
        }

        private static void InsertNoThinkDirective(List<ChatMessage> messages)
        {
            var systemIndex = messages.FindIndex(message => message.Role == ChatRole.System);
            if (systemIndex < 0)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, NoThinkDirective));
                return;
            }

            var systemText = string.Join(
                "\n",
                messages[systemIndex].Contents
                    .OfType<TextContent>()
                    .Select(content => content.Text)
                    .Where(static text => !string.IsNullOrWhiteSpace(text)));

            if (systemText.Contains(NoThinkDirective, StringComparison.Ordinal))
            {
                return;
            }

            systemText = string.IsNullOrWhiteSpace(systemText)
                ? NoThinkDirective
                : $"{systemText}\n{NoThinkDirective}";

            messages[systemIndex] = new ChatMessage(ChatRole.System, systemText);
        }

        private static string ExtractThinkTaggedReasoning(ref string content, ThinkStreamState state)
        {
            if (!state.InsideThink)
            {
                var startIndex = content.IndexOf(ThinkStartTag, StringComparison.OrdinalIgnoreCase);
                if (startIndex < 0)
                {
                    return string.Empty;
                }

                var textBeforeThink = content[..startIndex];
                content = content[(startIndex + ThinkStartTag.Length)..];
                state.InsideThink = true;

                var reasoning = ExtractThinkTaggedReasoning(ref content, state);
                content = textBeforeThink + content;
                return reasoning;
            }

            var endIndex = content.IndexOf(ThinkEndTag, StringComparison.OrdinalIgnoreCase);
            if (endIndex < 0)
            {
                var reasoning = content;
                content = string.Empty;
                return reasoning;
            }

            var reasoningBeforeEnd = content[..endIndex];
            content = content[(endIndex + ThinkEndTag.Length)..];
            state.InsideThink = false;
            return reasoningBeforeEnd;
        }
    }
}
