using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VllmChatClient.Test
{
    public class DeepseekR1Test
    {
        private readonly IChatClient _client;
        public DeepseekR1Test()
        {
            _client = new VllmDeepseekR1ChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", "", "deepseek-r1");
        }

        [Fact]
        public async Task ChatTest()
        {

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };

            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);

            Assert.Equal(1, res.Messages.Count);
        }

        [Fact]
        public async Task StreamChatTest()
        {

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            string res = string.Empty;
            string think = string.Empty;
            await foreach (ReasoningChatResponseUpdate update in _client.GetStreamingResponseAsync(messages))
            {
                var updateText = update.ToString();
                if (update is ReasoningChatResponseUpdate)
                {
                    if (update.Thinking)
                    {
                        think += updateText;
                    }
                    else
                    {
                        res += updateText;
                    }

                }
                
            }
            Assert.True(res != null);
            Assert.True(think != null);
        }
    }
}
