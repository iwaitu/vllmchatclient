using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{

    public class Qwen3ChatTest
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        private readonly ITestOutputHelper _output;

        // 端点和密钥配置
        private const string Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}";
        private string ApiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY") ?? "";
        private const string ModelId = "qwen3-32b";


        public Qwen3ChatTest(ITestOutputHelper testOutput)
        {
            _client = new VllmQwen3ChatClient(Endpoint, ApiKey, ModelId);
            _output = testOutput;
        }

        /// <summary>
        /// 使用 HttpClient 直接测试端点和 API Key 是否可用
        /// </summary>
        [Fact]
        public async Task TestEndpointWithHttpClient()
        {
            // 构建实际的 API 端点 URL
            string apiUrl = string.Format(Endpoint, "v1", "chat/completions");
            
            using var httpClient = new HttpClient();
            
            // 设置授权头
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            
            // 构建简单的聊天请求
            var requestPayload = new
            {
                model = ModelId,
                messages = new[]
                {
                    new { role = "user", content = "你好" }
                },
                stream = false,
                temperature = 0.7
            };
            
            try
            {
                // 发送 POST 请求
                var response = await httpClient.PostAsJsonAsync(apiUrl, requestPayload);
                
                // 验证响应状态
                Assert.True(response.IsSuccessStatusCode, 
                    $"API 请求失败。状态码: {response.StatusCode}, 原因: {response.ReasonPhrase}");
                
                // 读取响应内容
                var responseContent = await response.Content.ReadAsStringAsync();
                Assert.NotEmpty(responseContent);
                
                // 解析 JSON 响应
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;
                
                // 验证响应结构
                Assert.True(root.TryGetProperty("choices", out var choices), "响应缺少 'choices' 字段");
                Assert.True(choices.GetArrayLength() > 0, "choices 数组为空");
                
                // 提取回复内容
                var firstChoice = choices[0];
                Assert.True(firstChoice.TryGetProperty("message", out var message), "缺少 'message' 字段");
                Assert.True(message.TryGetProperty("content", out var content), "缺少 'content' 字段");
                
                var replyText = content.GetString();
                Assert.False(string.IsNullOrWhiteSpace(replyText), "模型回复内容为空");
                
                // 输出测试结果（用于调试）
                Console.WriteLine($"✓ 端点连接成功: {apiUrl}");
                Console.WriteLine($"✓ API Key 验证通过");
                Console.WriteLine($"✓ 模型回复: {replyText}");
                
                // 验证回复包含常见的问候语关键词
                var containsGreeting = replyText.Contains("你好") || 
                                       replyText.Contains("您好") || 
                                       replyText.Contains("Hi") ||
                                       replyText.Contains("Hello") ||
                                       replyText.Contains("帮助") ||
                                       replyText.Contains("什么") ||
                                       replyText.Contains("菲菲");
                
                Assert.True(containsGreeting, 
                    $"模型回复似乎不是有效的问候响应。实际回复: {replyText}");
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"HTTP 请求异常: {ex.Message}。请检查端点 URL 和网络连接。");
            }
            catch (JsonException ex)
            {
                Assert.Fail($"JSON 解析失败: {ex.Message}。响应格式可能不正确。");
            }
            catch (Exception ex)
            {
                Assert.Fail($"测试失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        [Fact]
        public async Task ChatTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲。"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            var options = new Qwen3ChatOptions
            {
                //NoThinking = false,
            };
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);

            Assert.Single(res.Messages);
            _output.WriteLine(res.Text);

        }

        [Fact]
        public async Task ExtractTags()
        {
            string text = "不动产登记资料查询，即查档业务，包括查询房屋、土地、车库车位等不动产登记结果，以及复制房屋、土地、车库车位等不动产登记原始资料。\n";

            var options = new Qwen3ChatOptions
            {
                NoThinking = true,
            };
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User,$"请为以下文本提取3个最相关的标签。用json格式返回，不要输出代码块。\n\n文本:{text}\n/no_think")
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            var match = Regex.Match(res.Messages.FirstOrDefault()?.Text, @"\s*(\{.*?\}|\[.*?\])\s*", RegexOptions.Singleline);
            Assert.True(match.Success);
            string json = match.Groups[1].Value;
            Assert.NotEmpty(json);
            _output.WriteLine(json);
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
            Qwen3ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)],
                //NoThinking = true,

            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            _output.WriteLine(res.Text);
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
            string reason = string.Empty;
            var options = new Qwen3ChatOptions
            {
                NoThinking = false,
            };
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
            Assert.True(res != null);
            _output.WriteLine($"思考过程：\n{reason}");
            _output.WriteLine($"最终答案：\n{res}");
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
            Qwen3ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)],
                NoThinking = false
            };
            string res = string.Empty;
            string reasoning = string.Empty;
            ReasoningChatResponseUpdate lastUpdate;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update is null) continue;
                if(update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    lastUpdate = reasoningUpdate;
                    if (reasoningUpdate.Thinking)
                    {
                        reasoning += reasoningUpdate.Text;
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
            _output.WriteLine("思考过程：");
            _output.WriteLine(reasoning);
            _output.WriteLine("最终回答：");
            _output.WriteLine(res);
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
            Qwen3ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)],
                NoThinking = true
            };
            string res = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                res += update;
            }
            Assert.True(res != null);
            var textContent = RemoveThinkTag(res);
            Assert.NotNull(textContent);
            Assert.All(textContent.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line); // 确保没有代码块
                Assert.DoesNotContain("```json", line); // 确保没有json代码块
            });

            _output.WriteLine(res);
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

        private static string RemoveThinkTag(string content)
        {
            // 移除 <think>...</think> 及其后连续的换行符
            return System.Text.RegularExpressions.Regex.Replace(
                content,
                "<think>.*?</think>\\n*",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline);
        }


        [Description("获取的天气情况")]
        static string GetWeather([Description("要查询的城市名称")]string city) =>  "It's raining";


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
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };
            var res = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 1);
            Assert.True(res.Messages[0].Contents.Count == 2);

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
            _output.WriteLine(answerText);
        }

        [Fact]
        public async Task TestJsonOutput()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用 codeblock。")
            };
            var options = new Qwen3ChatOptions
            {
                MaxOutputTokens = 100,
                NoThinking = true,
            };
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            _output.WriteLine(res.Text);
            
            // 确保输出是有效的JSON格式
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(res.Text);
                Assert.NotNull(json);
            }
            catch (System.Text.Json.JsonException)
            {
                Assert.Fail("输出的文本不是有效的JSON格式。");
            }
        }
    }
}
   
