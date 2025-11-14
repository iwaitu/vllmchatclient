using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI
{
    public class ReasoningChatResponseUpdate : ChatResponseUpdate
    {
        public bool Thinking { get; set; } = true;
        public string Reasoning { get; set; } = "";
        public string Anwser { get; set; } = "";
    }
}
