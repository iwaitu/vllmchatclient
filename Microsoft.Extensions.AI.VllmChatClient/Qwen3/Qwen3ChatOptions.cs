namespace Microsoft.Extensions.AI
{
    public class Qwen3ChatOptions : VllmChatOptions
    {
        public Qwen3ChatOptions()
        {
            ThinkingEnabled = true;
            TopP = 0.9f;
            TopK = 20;
            Temperature = 0.95f;
        }

        [Obsolete("Use ThinkingEnabled instead. NoThinking = true is equivalent to ThinkingEnabled = false.")]
        public bool NoThinking
        {
            get => !ThinkingEnabled;
            set => ThinkingEnabled = !value;
        }
    }
}
