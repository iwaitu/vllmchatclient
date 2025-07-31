using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace VllmChatClient.Test
{

    public class Qwen2507ChatTests
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        public Qwen2507ChatTests()
        {
            _client = new VllmQwen2507ChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", "", "qwen3-235b-a22b-instruct-2507");
        }



        [Fact]
        public async Task ChatTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲 /think"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            var options = new ChatOptions();
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);

            Assert.Equal(1, res.Messages.Count);

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
            var match = Regex.Match(res.Messages.FirstOrDefault()?.Text, @"\s*(\{.*?\}|\[.*?\])\s*", RegexOptions.Singleline);
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
            Assert.True(res.Messages.Count == 3);
            Assert.True(res.Messages.LastOrDefault()?.Text.Contains("下雨"));
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

            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                res += update.Text;

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
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                res += update;
            }

            Assert.True(res != null);
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
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                res += update;
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
