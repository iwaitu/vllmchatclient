using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class ClaudeTests
    {
        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;
        private readonly bool _skipTests;

        public ClaudeTests(ITestOutputHelper output)
        {
            _output = output;
            var apiKey = Environment.GetEnvironmentVariable("OPEN_ROUTE_API_KEY");
            _skipTests = string.IsNullOrWhiteSpace(apiKey);
            _client = new VllmClaudeChatClient("https://openrouter.ai/api/{0}/{1}", apiKey, "anthropic/claude-opus-4.6");
        }

        [Description("获取天气情况")]
        static string GetWeather([Description("城市名称")] string city) => $"{city} 气温35度，暴雨。";

        [Description("网络搜索工具，可以查询具体的地点、地址或即时信息")]
        static string Search([Description("需要搜索的问题或目的地")] string question)
        {
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [Description("根据具体地址搜索周边的书店")]
        static string FindBookStore([Description("需要搜索的具体地址/门牌号")] string dest)
        {
            return $"在 {dest} 附近100米有一家爱民书店。";
        }

        [Fact]
        public async Task ChatTest()
        {
            if (_skipTests) return;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。遇到问题请优先调用工具解决。"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };
            var chatOptions = new VllmChatOptions
            {
                ThinkingEnabled = true,
                MaxOutputTokens = 1024
            };
            var res = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.Equal(1, res.Messages.Count);

            var reasoningResponse = res as ReasoningChatResponse;
            if (reasoningResponse != null)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task StreamChatTest()
        {
            if (_skipTests) return;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };
            var chatOptions = new VllmChatOptions
            {
                ThinkingEnabled = true,
                MaxOutputTokens = 1024
            };

            string res = string.Empty;
            string think = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        think += update.Text;
                    }
                    else
                    {
                        res += update.Text;
                    }
                }
                else
                {
                    res += update.Text;
                }
            }

            Assert.NotNull(res);
            Assert.NotEmpty(res);

            _output.WriteLine($"Thinking: {think}");
            _output.WriteLine($"Response: {res}");
        }

        [Fact]
        public async Task ChatFunctionCallTest()
        {
            if (_skipTests) return;

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我想到那附近去买书。")
            };
            var chatOptions = new VllmChatOptions
            {
                ThinkingEnabled = true,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);

            var lastMessage = res.Messages.LastOrDefault();
            Assert.NotNull(lastMessage);
            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), $"Unexpected reply: '{res.Text}'");
            _output.WriteLine($"Response: {res.Text}");
        }

        

        [Fact]
        public async Task StreamChatParallelFunctionCallTest()
        {
            if (_skipTests) return;

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            var chatOptions = new VllmChatOptions
            {
                ThinkingEnabled = true,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningMessage)
                {
                    if (reasoningMessage.Thinking)
                    {
                        reason += reasoningMessage.Text;
                    }
                    else
                    {
                        res += reasoningMessage.Text;
                    }
                }
                else
                {
                    res += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            bool hasWeather = res.Contains("下雨") || res.Contains("雨") || res.Contains("35度");
            bool hasLocation = res.Contains("方圆广场") || res.Contains("站前路");

            Assert.True(hasWeather || hasLocation, $"Unexpected reply: '{res}'");
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }
    }
}
