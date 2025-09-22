using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI
{
    public class ReasoningChatResponse: ChatResponse
    {
        public ReasoningChatResponse(ChatMessage message, string reasoning)
            : base(message)
        {
            Reason = reasoning ?? throw new ArgumentNullException(nameof(reasoning));
        }
        public string Reason { get; }
    }
}
