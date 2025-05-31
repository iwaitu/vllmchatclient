using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VllmChatClient.Test
{
    public class QwQChatTest
    {
        private readonly IChatClient _client;
        public QwQChatTest()
        {
            _client = new VllmQwqChatClient("https://localhost:8000/v1/{1}", "", "qwq");
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
        public async Task ExtractTags()
        {
            string text = "不动产登记资料查询，即查档业务，包括查询房屋、土地、车库车位等不动产登记结果，以及复制房屋、土地、车库车位等不动产登记原始资料。\n";

           
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User,$"请为以下文本提取3个最相关的标签。用json格式返回，不要其他说明。\n\n文本:{text}")
            };

            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);
            var match = Regex.Match(res.Messages.FirstOrDefault()?.Text, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline);
            Assert.True(match.Success);
            string json = match.Groups[1].Value;
            Assert.NotEmpty(json);
        }

        [Fact]
        public async Task ChatFunctionCallTest()
        {
            
            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"我需要带伞吗？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
        }

        [Description("Gets the weather")]
        static string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

        [Fact]
        public async Task StreamChatTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                res += update;
            }
            Assert.True(res != null);
        }

        [Fact]
        public async Task StreamChatFunctionCallTest()
        {
            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"我需要带伞吗？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            string res = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                res += update;
            }
            Assert.True(res != null);
        }
    }
}
