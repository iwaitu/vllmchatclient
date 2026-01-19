namespace Microsoft.Extensions.AI.VllmChatClient.Glm4
{
    public class GlmChatOptions : ChatOptions
    {
        /// <summary>
        /// Enable/disable GLM thinking chain output.
        /// Default is disabled.
        /// </summary>
        public bool ThinkingEnabled { get; set; } = false;
    }
}
