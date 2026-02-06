namespace Microsoft.Extensions.AI.VllmChatClient.Kimi
{
    public class KimiChatOptions : ChatOptions
    {
        /// <summary>
        /// Enable/disable Kimi thinking chain output.
        /// Default is enabled.
        /// </summary>
        public bool ThinkingEnabled { get; set; } = true;
    }
}
