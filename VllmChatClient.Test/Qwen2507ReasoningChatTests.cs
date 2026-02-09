using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace VllmChatClient.Test
{

    public class Qwen2507ReasoningChatTests
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        private readonly bool _skipTests;
        public Qwen2507ReasoningChatTests()
        {
            var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            var runExternal = Environment.GetEnvironmentVariable("VLLM_RUN_EXTERNAL_TESTS");
            _skipTests = runExternal != "1" || string.IsNullOrWhiteSpace(cloud_apiKey);
            _client = new VllmQwen2507ReasoningChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "qwen3-235b-a22b-thinking-2507");
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
           
            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);

            Assert.Equal(1, res.Messages.Count);
            Assert.True(res.Text.Contains("菲菲"));


        }

        [Fact]
        public async Task ExtractTags()
        {
            if (_skipTests)
            {
                return;
            }
            string text = "不动产登记资料查询，即查档业务，包括查询房屋、土地、车库车位等不动产登记结果，以及复制房屋、土地、车库车位等不动产登记原始资料。\n";

            var options = new ChatOptions
            {
                Temperature = 0.5f
            };
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User,$"请为以下文本提取3个最相关的标签。用json格式返回，不要输出代码块。\n\n文本:{text}")
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            var match = Regex.Match(res.Messages.FirstOrDefault()?.Text, @"\s*(\{.*?\}|\[.*?\])\s*", RegexOptions.Singleline);
            Assert.True(match.Success);
            string json = match.Groups[1].Value;
            Assert.NotEmpty(json);
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"我需要带伞吗？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 1);
            Assert.True(res.Messages[0].Contents.Count == 1);
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
                new ChatMessage(ChatRole.System ,"我是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            string res = string.Empty;
            string cot = string.Empty;
            var options = new ChatOptions();
            
            await foreach (ReasoningChatResponseUpdate update in _client.GetStreamingResponseAsync(messages, options))
            {
                if (update.Thinking)
                {
                    cot += update.Text;
                }
                else
                {
                    res += update.Text;
                }

            }
            Assert.True(cot != null);
            Assert.True(res != null);
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            string cot = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if(update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if(reasoningUpdate.Thinking)
                        cot += reasoningUpdate.Text;
                    else
                        res += reasoningUpdate.Text;
                }
                
            }
            Assert.True(cot != null);
            Assert.True(res != null);
        }

        [Fact]
        public async Task StreamChatJsonoutput()
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用 codeblock。")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (!reasoningUpdate.Thinking)
                        res += reasoningUpdate.Text;
                }
            }
            Assert.True(res != null);
            var textContent = res;
            Assert.NotNull(textContent);
            Assert.All(textContent.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line); // 确保没有代码块
                Assert.DoesNotContain("```json", line); // 确保没有json代码块
            });
            // 确保输出是有效的JSON格式
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(textContent);
                Assert.NotNull(json);
            }
            catch (System.Text.Json.JsonException)
            {
                Assert.Fail("输出的文本不是有效的JSON格式。");
            }
        }

        [Description("获取南宁的天气情况")]
        static string GetWeather() => "It's raining";


        [Description("Searh")]
        static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            var res = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 1);
            Assert.True(res.Messages[0].Contents.Count == 1);

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
                var anwser = string.Empty;
                if ("GetWeather" == functionCall.Name)
                {
                    anwser = "30度，天气晴朗。";
                }
                else
                {
                    anwser = "在青秀区方圆广场附近站前路1号。";
                }

                var functionResult = new FunctionResultContent(functionCall.CallId, anwser);
                var contentList = new List<AIContent>();
                contentList.Add(functionResult);
                var functionResultMessage = new ChatMessage(ChatRole.Tool, contentList);
                messages.Add(functionResultMessage);
            }


            var result = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(result);
            Assert.Single(result.Messages);

            foreach (var content in result.Messages[0].Contents)
            {
                var funcMsg = new ChatResponse();
                var msgContent = new ChatMessage();
                msgContent.Contents.Add(content);
                funcMsg.Messages.Add(msgContent);
                messages.AddMessages(funcMsg);

                Assert.True(content is FunctionCallContent);
                var functionCall = content as FunctionCallContent;
                Assert.NotNull(functionCall);
                var anwser = string.Empty;
                if ("GetWeather" == functionCall.Name)
                {
                    anwser = "30度，天气晴朗。";
                }
                else
                {
                    anwser = "在青秀区方圆广场附近站前路1号。";
                }

                var functionResult = new FunctionResultContent(functionCall.CallId, anwser);
                var contentList = new List<AIContent>();
                contentList.Add(functionResult);
                var functionResultMessage = new ChatMessage(ChatRole.Tool, contentList);
                messages.Add(functionResultMessage);
            }

            var finalresult = await _client.GetResponseAsync(messages, chatOptions);
            var answerText = finalresult.Messages[0].Contents
                                   .OfType<TextContent>()
                                   .FirstOrDefault()?.Text;

            Assert.False(string.IsNullOrWhiteSpace(answerText));
        }

        [Fact]
        public async Task TestJsonOutput()
        {
            if (_skipTests)
            {
                return;
            }
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用 codeblock。")
            };
            var options = new ChatOptions
            {
                MaxOutputTokens = 100
            };
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            var textContent = res.Messages[0].Contents.OfType<TextContent>().FirstOrDefault();
            Assert.NotNull(textContent);
            Assert.All(textContent.Text.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line); // 确保没有代码块
                Assert.DoesNotContain("```json", line); // 确保没有json代码块
            });
            // 确保输出是有效的JSON格式
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(textContent.Text);
                Assert.NotNull(json);
            }
            catch (System.Text.Json.JsonException)
            {
                Assert.Fail("输出的文本不是有效的JSON格式。");
            }
        }
    }
}
