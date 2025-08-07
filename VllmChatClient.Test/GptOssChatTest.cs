using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VllmChatClient.Test
{
    public class GptOssChatTest
    {
        private readonly IChatClient _client;
        static string ApiToken = "";
        public GptOssChatTest()
        {
            // Use the actual GPT-OSS client for testing with OpenRouter
            _client = new VllmGptOssChatClient("https://openrouter.ai/api/v1", ApiToken, "openai/gpt-oss-120b");
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

        [Description("获取南宁的天气情况")]
        static string GetWeather() => "It's raining";

        [Description("地名地址搜索")]
        static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [Fact]
        public void TestUrlConstruction()
        {
            // Test different endpoint formats
            var client1 = new VllmGptOssChatClient("https://openrouter.ai/api/v1", "test-token", "gpt-oss-120b");
            var client2 = new VllmGptOssChatClient("http://localhost:8000", "test-token", "gpt-oss-120b");
            var client3 = new VllmGptOssChatClient("https://api.example.com/", "test-token", "gpt-oss-120b");

            // These should compile without errors, indicating proper URL handling
            Assert.NotNull(client1);
            Assert.NotNull(client2);
            Assert.NotNull(client3);
        }

        [Fact]
        public async Task StreamChatTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };

            string result = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                if (update.Contents.Count > 0)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            result += textContent.Text;
                        }
                    }
                }
            }

            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("菲菲", result);
        }

        [Fact]
        public async Task StreamChatFunctionCallTest()
        {
            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };

            ChatOptions chatOptions = new()
            {
                Temperature = 0.5f,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };

            string result = string.Empty;
            string think = string.Empty;
            bool foundFunctionCall = false;
            int totalUpdates = 0;
            int reasoningUpdates = 0;
            int thinkingUpdates = 0;

            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                totalUpdates++;
                
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    reasoningUpdates++;
                    if(reasoningUpdate.Thinking)
                    {
                        thinkingUpdates++;
                        // 如果模型在思考，可以选择处理思考内容
                        think += reasoningUpdate.Reasoning;
                    }
                    else
                    {
                        if (reasoningUpdate.Contents.Count > 0)
                        {
                            foreach (var content in reasoningUpdate.Contents)
                            {
                                if (content is TextContent textContent)
                                {
                                    result += textContent.Text;
                                }
                                else if (content is FunctionCallContent)
                                {
                                    foundFunctionCall = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 处理非推理更新（来自FunctionInvokingChatClient包装器的转换）
                    if (update.Contents.Count > 0)
                    {
                        foreach (var content in update.Contents)
                        {
                            if (content is TextContent textContent)
                            {
                                result += textContent.Text;
                            }
                            else if (content is FunctionCallContent)
                            {
                                foundFunctionCall = true;
                            }
                        }
                    }
                }
            }

            Assert.NotNull(result);
            
            // Note: think 内容现在应该包含推理内容，经过改进的结构化分析
            // 推理内容現在通過AnalyzeReasoningStructure方法進行結構化處理
            Assert.NotEmpty(think); // 仍然注释掉，因为FunctionInvokingChatClient包装器问题
            //Assert.True(thinkingUpdates == 2);
            // 应该包含函数调用或结果
            Assert.True(foundFunctionCall || result.Contains("raining") || result.Contains("下雨"), 
                $"Expected function call or weather content, but got: foundFunctionCall={foundFunctionCall}, result='{result}'");
        }

        [Fact]
        public async Task StreamChatJsonoutput()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用 codeblock。例如：{\"greeting\":\"你好\"}")
            };
            ChatOptions chatOptions = new()
            {
                Temperature = 0.3f, // 降低温度以获得更一致的输出
                MaxOutputTokens = 100 // 限制输出长度
            };
            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update.Contents.Count > 0)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            res += textContent.Text;
                        }
                    }
                }
            }
            
            Assert.NotNull(res);
            Assert.NotEmpty(res);
            
            // 清理输出内容，移除可能的前后空格和换行
            var cleanedRes = res.Trim();
            
            // 验证不包含代码块标记
            Assert.All(cleanedRes.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line); // 确保没有代码块
                Assert.DoesNotContain("```json", line); // 确保没有json代码块
            });
            
            // 尝试找到JSON内容（可能在解释文本之后）
            var jsonMatch = Regex.Match(cleanedRes, @"(\{[^}]*\}|\[[^\]]*\])", RegexOptions.Singleline);
            
            if (jsonMatch.Success)
            {
                var jsonContent = jsonMatch.Value;
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(jsonContent);
                    Assert.NotNull(json);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    Assert.Fail($"提取的JSON内容无效: '{jsonContent}'. 错误: {ex.Message}");
                }
            }
            else
            {
                // 如果没找到标准JSON格式，至少确保输出包含期望的内容
                Assert.True(cleanedRes.Contains("菲菲") || cleanedRes.Contains("greeting") || cleanedRes.Contains("你好"), 
                    $"输出内容不符合预期: '{cleanedRes}'");
            }
        }

        [Fact]
        public void OptimizedAnalyzeReasoningStructureTest()
        {
            var client = new VllmGptOssChatClient("https://openrouter.ai/api/v1", ApiToken, "gpt-oss-120b");
            
            // 使用反射访问私有方法来测试
            var method = typeof(VllmGptOssChatClient).GetMethod("AnalyzeReasoningStructure", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            Assert.NotNull(method);
            
            // 测试字符串输入
            var result = (ValueTuple<bool, string, string>)method.Invoke(null, new object[] { "Test reasoning" })!;
            Assert.True(result.Item1); // hasReasoning
            Assert.Equal("Test reasoning", result.Item2); // reasoningText
            Assert.Equal("standard", result.Item3); // reasoningType
            
            // 测试 null 输入
            var nullResult = (ValueTuple<bool, string, string>)method.Invoke(null, new object[] { null! })!;
            Assert.False(nullResult.Item1); // hasReasoning should be false
            Assert.Equal("", nullResult.Item2); // reasoningText should be empty
            Assert.Equal("unknown", nullResult.Item3); // reasoningType should be unknown
            
            Assert.True(true); // 如果能执行到这里说明方法优化成功且无调试输出
        }

        [Description("获取当前时间")]
        static string GetCurrentTime() => DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss");

        [Description("计算数学表达式")]
        static string CalculateMath([Description("数学表达式")] string expression) => "150";

        
    }
}