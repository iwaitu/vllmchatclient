using Microsoft.Extensions.AI.VllmChatClient.Kimi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI.VllmChatClient.Minimax
{

    public class VllmMiniMacChatClient : VllmBaseChatClient
    {
        public VllmMiniMacChatClient(string endpoint, string? token = null, string? modelId = "kimi-k2-thinking", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        
    }
}
