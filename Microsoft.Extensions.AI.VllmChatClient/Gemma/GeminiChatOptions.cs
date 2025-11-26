using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI.VllmChatClient.Gemma
{
    public class GeminiChatOptions : ChatOptions
    {
        public GeminiReasoningLevel ReasoningLevel { get; set; } = GeminiReasoningLevel.Normal;
    }

    public enum GeminiReasoningLevel
    {
        /// <summary>
        /// Basic reasoning level.
        /// </summary>
        Low,
        /// <summary>
        /// Advanced reasoning level.
        /// </summary>
        Normal,

    }
}
