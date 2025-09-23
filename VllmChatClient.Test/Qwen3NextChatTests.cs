using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace VllmChatClient.Test
{

    public class Qwen3NextChatTests
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        
        public Qwen3NextChatTests()
        {
            var apiKey = Environment.GetEnvironmentVariable("VLLM_API_KEY");
            var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            //_client = new VllmQwen3NextChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "qwen3-next-80b-a3b-thinking");
            //_client = new VllmQwen3NextChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "qwen3-next-80b-a3b-instruct");
            _client = new VllmQwen3NextChatClient("http://localhost:8000/v1/{1}", apiKey, "qwen3-next-80b-a3b-instruct");
        }



        [Fact]
        public async Task ChatTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲 "),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            var options = new ChatOptions();
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages); // 使用 Assert.Single 替代 Assert.Equal(1, ...)

            Assert.True(res.Text.Contains("菲菲"));
            if (res.ModelId.Contains("thinking"))
            {
                var reasonResponse = res as ReasoningChatResponse;
                Assert.NotNull(reasonResponse.Reason);
            }
            

        }


        [Fact]
        public async Task ExtractTags()
        {
            string text = "不动产登记资料查询，即查档业务，包括查询房屋、土地、车库车位等不动产登记结果，以及复制房屋、土地、车库车位等不动产登记原始资料。\n";

            var options = new ChatOptions();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User,$"请为以下文本提取3个最相关的标签。用json格式返回，不要输出代码块。\n\n文本:{text}")
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);

            // 修复可能的 null 引用警告
            var firstMessage = res.Messages.FirstOrDefault();
            Assert.NotNull(firstMessage);
            var messageText = firstMessage.Text;
            Assert.NotNull(messageText);

            var match = Regex.Match(messageText, @"\s*(\{.*?\}|\[.*?\])\s*", RegexOptions.Singleline);
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
            Assert.Equal(3, res.Messages.Count);

            // 使用 Assert.Contains 替代 Assert.True(...Contains(...))
            var lastMessage = res.Messages.LastOrDefault();
            Assert.NotNull(lastMessage);
            Assert.Contains("下雨", lastMessage.Text ?? string.Empty);
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
            var options = new ChatOptions();
            string reason = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                bool isThinkingModel = update.ModelId.Contains("thinking") ? true : false;
                if (isThinkingModel && update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate;
                    }
                    else
                    {
                        res += reasoningUpdate;
                    }
                }
                else
                {
                    res += update;
                }

            }
            Assert.NotNull(res);
            Assert.NotEmpty(res);
            Assert.True(res.Contains("菲菲"));
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
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？")
                //new ChatMessage(ChatRole.User,"南宁火车站在哪里？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                bool isThinkingModel = update?.ModelId?.Contains("thinking") ?? false;
                if (isThinkingModel && update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate;
                    }
                    else
                    {
                        res += reasoningUpdate;
                    }
                }
                else
                {
                    res += update;
                }
            }

            Assert.NotNull(res);
            Assert.NotEmpty(res);
        }

        [Fact]
        public async Task StreamChatJsonoutput()
        {
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
                //Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                bool isThinkingModel = update.ModelId.Contains("thinking") ? true : false;
                if (isThinkingModel && update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate;
                    }
                    else
                    {
                        res += reasoningUpdate;
                    }
                }
                else
                {
                    res += update;
                }
            }
            Assert.NotNull(res);
            Assert.NotEmpty(res);

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
        static string GetWeather() => "现在正在下雨。";


        [Description("Searh")]
        static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [Fact]
        public async Task ChatManualFunctionCallTest()
        {


            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            var res = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            Assert.Single(res.Messages[0].Contents);

            foreach (var content in res.Messages[0].Contents)
            {
                var funcMsg = new ChatResponse();
                var msgContent = new ChatMessage();
                msgContent.Contents.Add(content);
                funcMsg.Messages.Add(msgContent);
                messages.AddMessages(funcMsg);

                Assert.IsType<FunctionCallContent>(content);
                var functionCall = (FunctionCallContent)content;
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

            var answerText = result.Messages[0].Contents
                                   .OfType<TextContent>()
                                   .FirstOrDefault()?.Text;

            Assert.False(string.IsNullOrWhiteSpace(answerText));
        }

        [Fact]
        public async Task TestJsonOutput()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用 codeblock。")
            };
            var options = new ChatOptions
            {
                MaxOutputTokens = 100,
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
