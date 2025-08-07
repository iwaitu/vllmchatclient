using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI.VllmChatClient.GptOss
{
    public class GptOssChatOptions : ChatOptions
    {
        public GptOssReasoningLevel ReasoningLevel { get; set; } = GptOssReasoningLevel.Medium;
    }

    public enum GptOssReasoningLevel
    {
        /// <summary>
        /// Basic reasoning level.
        /// </summary>
        Low,
        /// <summary>
        /// Advanced reasoning level.
        /// </summary>
        Medium,
        /// <summary>
        /// Expert reasoning level.
        /// </summary>
        High
    }
}
