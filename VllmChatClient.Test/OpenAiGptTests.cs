using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class OpenAiGptTests
    {
        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;
        private readonly bool _skipTests;

        public OpenAiGptTests(ITestOutputHelper output)
        {
            _output = output;
            var apiKey = Environment.GetEnvironmentVariable("OPEN_ROUTE_API_KEY");
            _skipTests = string.IsNullOrWhiteSpace(apiKey);
            _client = new VllmOpenAiGptClient("https://openrouter.ai/api/v1", apiKey, "openai/gpt-5.2-codex");
        }

        [System.ComponentModel.Description("获取天气情况")]
        static string GetWeather([System.ComponentModel.Description("城市名称")] string city) => $"{city} 气温35度，暴雨。";

        [System.ComponentModel.Description("网络搜索工具，可以查询具体的地点、地址或即时信息")]
        static string Search([System.ComponentModel.Description("需要搜索的问题或目的地")] string question)
        {
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [System.ComponentModel.Description("根据具体地址搜索周边的书店")]
        static string FindBookStore([System.ComponentModel.Description("需要搜索的具体地址/门牌号")] string dest)
        {
            return $"在 {dest} 附近100米有一家爱民书店。";
        }

        [Fact]
        public async Task ChatTest()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };
            var options = new OpenAiGptChatOptions
            {
                Temperature = 0.5f,
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                MaxOutputTokens = 1024
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Equal(1, res.Messages.Count);

            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task StreamChatTest()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };
            var options = new OpenAiGptChatOptions
            {
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                MaxOutputTokens = 1024
            };

            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        res += reasoningUpdate.Text;
                    }
                }
                else
                {
                    res += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }

        [Fact]
        public async Task ChatFunctionCallTest()
        {
            if (_skipTests)
            {
                return;
            }

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我想到那附近去买书。")
            };
            var options = new OpenAiGptChatOptions
            {
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            var res = await client.GetResponseAsync(messages, options);
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
        public async Task StreamChatFunctionCallTest()
        {
            if (_skipTests)
            {
                return;
            }

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            var options = new OpenAiGptChatOptions
            {
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        res += reasoningUpdate.Text;
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

        [Fact]
        public async Task ChatManualFunctionCallTest()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            var options = new OpenAiGptChatOptions
            {
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 1);
            Assert.True(res.Messages[0].Contents.Count >= 1);
            string reason = string.Empty;
            var reasoningResponse1 = res as ReasoningChatResponse;
            reason = reasoningResponse1.Reason;
            foreach (var content in res.Messages[0].Contents)
            {
                var funcMsg = new ChatResponse();
                var msgContent = new ChatMessage();
                msgContent.Contents.Add(content);
                funcMsg.Messages.Add(msgContent);
                messages.AddMessages(funcMsg);

                Assert.True(content is FunctionCallContent);
                var functionCall = content as FunctionCallContent;
                Assert.NotNull(functionCall);
                string answer;
                if ("GetWeather" == functionCall.Name)
                {
                    answer = "南宁 气温35度，暴雨。";
                }
                else if ("FindBookStore" == functionCall.Name)
                {
                    answer = "在 南宁市青秀区方圆广场北面站前路1号。 附近100米有一家爱民书店。";
                }
                else
                {
                    answer = "南宁市青秀区方圆广场北面站前路1号。";
                }

                var functionResult = new FunctionResultContent(functionCall.CallId, answer);
                var contentList = new List<AIContent> { functionResult };
                var functionResultMessage = new ChatMessage(ChatRole.Tool, contentList);
                messages.Add(functionResultMessage);
            }

            var result = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(result);
            Assert.Single(result.Messages);

            var answerText = result.Messages[0].Contents
                                   .OfType<TextContent>()
                                   .FirstOrDefault()?.Text;

            Assert.False(string.IsNullOrWhiteSpace(answerText));
            if (result is ReasoningChatResponse reasoningResponse)
            {
                reason += reasoningResponse.Reason;
                
            }
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {answerText}");
        }

        [Fact]
        public async Task StreamChatManualFunctionCallTest()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            var options = new OpenAiGptChatOptions
            {
                ReasoningLevel = OpenAiGptReasoningLevel.High,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)],
                MaxOutputTokens = 1024
            };

            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                if (update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var fc in update.Contents.OfType<FunctionCallContent>())
                    {
                        messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));

                        string answer;
                        if (fc.Name == "GetWeather")
                        {
                            answer = GetWeather("南宁");
                        }
                        else if (fc.Name == "FindBookStore")
                        {
                            answer = FindBookStore("南宁市青秀区方圆广场北面站前路1号。");
                        }
                        else
                        {
                            answer = Search("南宁火车站在哪里？");
                        }

                        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(fc.CallId, answer)]));
                    }
                    continue;
                }

                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        res += reasoningUpdate.Text;
                    }
                }
                else
                {
                    res += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }
    }
}
