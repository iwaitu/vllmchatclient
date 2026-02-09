using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{

    public class Qwen3NextChatTests
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        private readonly ITestOutputHelper _output;
        private readonly bool _skipTests;

        public Qwen3NextChatTests(ITestOutputHelper output)
        {
            //var apiKey = Environment.GetEnvironmentVariable("VLLM_API_KEY");
            var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            var runExternal = "1";
            _skipTests = runExternal != "1" || string.IsNullOrWhiteSpace(cloud_apiKey);
            //_client = new VllmQwen3NextChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "qwen3-next-80b-a3b-thinking");
            _client = new VllmQwen3NextChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "qwen3-next-80b-a3b-instruct");
            //_client = new VllmQwen3NextChatClient("http://localhost:8000/v1/{1}", apiKey, "qwen3-next-80b-a3b-instruct");
            _output = output;
        }

        [Description("搜索周边的书店")]
        static string FindBookStore([Description("需要搜索的具体地址/门牌号")] string dest)
        {
            functionCallTime += 1;
            return "附近100米有一家爱民书店。";
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
                Assert.NotNull(reasonResponse?.Reason);
            }
            

        }


        [Fact]
        public async Task ExtractTags()
        {
            if (_skipTests)
            {
                return;
            }
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
        public async Task ChatSerialFunctionCallTest()
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我想到那附近去买书。")               //串行调用两个函数
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);

            // 最后一条回复通常是助手文本，包含天气信息
            var lastMessage = res.Messages.LastOrDefault();
            Assert.NotNull(lastMessage);
            var lastText = lastMessage.Contents.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), $"Unexpected reply: '{lastText}'");  //串行任务
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task ChatParallelFunctionCallTest()
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲。"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？")               //并行调用两个函数
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);

            // 最后一条回复通常是助手文本，包含天气信息
            var lastMessage = res.Messages.LastOrDefault();
            Assert.NotNull(lastMessage);
            var lastText = lastMessage.Contents.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            Assert.True(res.Text.Contains("下雨") || res.Text.Contains("雨"), $"Unexpected reply: '{lastText}'");  //并行任务
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            string res = string.Empty;
            var options = new ChatOptions();
            string reason = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                // 累积文本内容（仅 TextContent 更稳健）
                foreach (var text in update.Contents.OfType<TextContent>())
                {
                    res += text.Text;
                }
            }
            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.Contains("菲菲", res);
        }

        [Fact]
        public async Task StreamChatParallelFunctionCallTest()
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
                //new ChatMessage(ChatRole.User,"南宁火车站在哪里？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
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
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.True(res.Contains("下雨") || res.Contains("雨"), $"Unexpected reply: '{res}'");  //并行任务
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }

        [Fact]
        public async Task StreamChatSerialFunctionCallTest()
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
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我想到那附近去买书。")
                //new ChatMessage(ChatRole.User,"南宁火车站在哪里？")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
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
            }

            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.True(res.Contains("爱民书店") || res.Contains("100米"), $"Unexpected reply: '{res}'");  //串行任务
        }

        [Fact]
        public async Task StreamChatManualFunctionCallTest()
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
                if(update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var fc in update.Contents.OfType<FunctionCallContent>())
                    {
                        Assert.NotNull(fc);
                        messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));

                        string json = System.Text.Json.JsonSerializer.Serialize(
                            fc.Arguments,
                            new JsonSerializerOptions
                            {
                                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                        if(fc.Name == "GetWeather")
                        {
                            var result = GetWeather();
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(fc.CallId, result)]));
                            continue;
                        }else if(fc.Name == "Search")
                        {
                            var args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                            Assert.NotNull(args);
                            Assert.True(args.ContainsKey("question"));
                            var result = Search(args["question"]);
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(fc.CallId, result)]));
                            continue;
                        }
                    }
                }
                else
                {
                    foreach (var text in update.Contents.OfType<TextContent>())
                    {
                        res += text.Text;
                    }
                }
                    
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
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
                //Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            string res = string.Empty;
            string reason = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if(update is ReasoningChatResponseUpdate reasonUpdate)
                {
                    if(reasonUpdate.Thinking)
                        reason += reasonUpdate.Text;
                    else
                        res += reasonUpdate.Text;
                }
                else
                {
                    res += update.Text;
                }
            }
            Assert.False(string.IsNullOrWhiteSpace(res));

            // 验证不包含代码块标记
            Assert.All(res.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line);
                Assert.DoesNotContain("```json", line);
            });

            // 提取并验证 JSON 片段
            var jsonMatch = Regex.Match(res, @"(\{[^}]*\}|\[[^\]]*\])", RegexOptions.Singleline);
            Assert.True(jsonMatch.Success, $"未找到JSON片段: '{res}'");
            var jsonText = jsonMatch.Value;
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(jsonText);
                Assert.NotNull(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                Assert.Fail($"输出的文本不是有效的JSON格式。内容: '{jsonText}', 错误: {ex.Message}");
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
            if (_skipTests)
            {
                return;
            }


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

            // 至少应包含一个函数调用
            var functionCalls = res.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
            Assert.NotEmpty(functionCalls);

            foreach (var functionCall in functionCalls)
            {
                var funcMsg = new ChatResponse();
                var msgContent = new ChatMessage();
                msgContent.Contents.Add(functionCall);
                funcMsg.Messages.Add(msgContent);
                messages.AddMessages(funcMsg);

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
                MaxOutputTokens = 100,
            };
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            var textContent = res.Messages[0].Contents.OfType<TextContent>().FirstOrDefault();
            Assert.NotNull(textContent);

            // 验证不包含代码块标记
            Assert.All(textContent.Text.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line);
                Assert.DoesNotContain("```json", line);
            });

            // 从文本中提取 JSON 片段并验证
            var jsonMatch = Regex.Match(textContent.Text, @"(\{[^}]*\}|\[[^\]]*\])", RegexOptions.Singleline);
            Assert.True(jsonMatch.Success, $"未找到JSON片段: '{textContent.Text}'");
            var jsonText = jsonMatch.Value;
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(jsonText);
                Assert.NotNull(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                Assert.Fail($"输出的文本不是有效的JSON格式。内容: '{jsonText}', 错误: {ex.Message}");
            }
        }
    }
}
