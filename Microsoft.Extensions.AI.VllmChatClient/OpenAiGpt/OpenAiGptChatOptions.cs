namespace Microsoft.Extensions.AI
{
    public class OpenAiGptChatOptions : ChatOptions
    {
        public OpenAiGptReasoningLevel ReasoningLevel { get; set; } = OpenAiGptReasoningLevel.Medium;
        public bool ExcludeReasoning { get; set; }
    }

    public enum OpenAiGptReasoningLevel
    {
        Low,
        Medium,
        High
    }
}
