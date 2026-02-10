using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class DeepseekV3Tests
    {
        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;

        public DeepseekV3Tests(ITestOutputHelper output)
        {
            _output = output;
            var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            _client = new VllmDeepseekV3ChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "deepseek-v3.2");
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
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。遇到问题请优先调用工具解决。"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };
            var chatOptions = new VllmChatOptions
            {
               ThinkingEnabled = true
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
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "你是谁？")
            };

            string res = string.Empty;
            string think = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
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
            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我想到那附近去买书。")               //串行调用两个函数
            };
            var chatOptions = new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
            };
            
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);

            // 最后一条回复通常是助手文本
            var lastMessage = res.Messages.LastOrDefault();
            Assert.NotNull(lastMessage);
            var lastText = lastMessage.Contents.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
            if(res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), $"Unexpected reply: '{lastText}'");  //串行任务
            _output.WriteLine($"Response: {res.Text}");
        }
        
        [Fact]
        public async Task StreamChatParallelFunctionCallTest()
        {
            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new ChatMessage(ChatRole.User,"南宁火车站在哪里？我出门需要带伞吗？")
            };
            var chatOptions = new ChatOptions
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
                else
                {
                   res += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
             // 简单的并行检查
            bool hasWeather = res.Contains("下雨") || res.Contains("雨") || res.Contains("35度");
            bool hasLocation = res.Contains("方圆广场") || res.Contains("站前路");
            
            Assert.True(hasWeather || hasLocation, $"Unexpected reply: '{res}'");
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }

        [Fact]
        public async Task StreamChatManualFunctionCallTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            var chatOptions = new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };

            string res = string.Empty;
            string reason = string.Empty;
            
            // 缓冲区，用于聚合流式传输中的工具调用
            var toolCallsBuffer = new List<FunctionCallContent>();

            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                // 收集工具调用
                var foundToolCalls = update.Contents.OfType<FunctionCallContent>().ToList();
                if (foundToolCalls.Count > 0)
                {
                    toolCallsBuffer.AddRange(foundToolCalls);
                }

                // 收集文本和推理内容
                foreach (var text in update.Contents.OfType<TextContent>())
                {
                    if (update is ReasoningChatResponseUpdate reasoningUpdate && reasoningUpdate.Thinking)
                    {
                        reason += text.Text;
                    }
                    else
                    {
                        res += text.Text;
                    }
                }
            }
            
            _output.WriteLine($"[Stream] First round response: {res}");
            if (toolCallsBuffer.Count > 0)
            {
                _output.WriteLine($"[Stream] Tool calls: {toolCallsBuffer.Count}");
                foreach(var tc in toolCallsBuffer) _output.WriteLine($"  - {tc.Name}({JsonSerializer.Serialize(tc.Arguments)})");

                // 1. 将完整的 Assistant 消息（包含所有工具调用）添加到历史记录
                messages.Add(new ChatMessage(ChatRole.Assistant, toolCallsBuffer.Cast<AIContent>().ToList()));

                // 2. 执行工具并添加 Tool 消息
                foreach (var fc in toolCallsBuffer)
                {
                    _output.WriteLine($"Processing tool: {fc.Name}");
                    string json = JsonSerializer.Serialize(
                            fc.Arguments,
                            new JsonSerializerOptions
                            {
                                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                    
                    string resultString = string.Empty;
                    if (fc.Name == "GetWeather")
                    {
                        resultString = GetWeather("南宁");
                    }
                    else if (fc.Name == "Search")
                    {
                        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (args != null && args.ContainsKey("question"))
                        {
                            resultString = Search(args["question"]);
                        }
                        else
                        {
                            resultString = "参数错误";
                        }
                    }

                    if (!string.IsNullOrEmpty(resultString))
                    {
                        _output.WriteLine($"Tool result: {resultString}");
                        messages.Add(new ChatMessage(
                            ChatRole.Tool,
                            [new FunctionResultContent(fc.CallId, resultString)]));
                    }
                }

                // 调试输出消息历史
                _output.WriteLine("--- Message History ---");
                foreach (var m in messages)
                {
                    _output.WriteLine($"[{m.Role}] {string.Join(", ", m.Contents.Select(c => c.ToString()))}");
                }
                _output.WriteLine("-----------------------");

                // 3. 再次请求模型以获取最终响应
                var finalRes = await _client.GetResponseAsync(messages, chatOptions);
                res += finalRes.Text; // 补充最终回答
            }

            _output.WriteLine($"Reasoning: {reason}");
            _output.WriteLine($"Response: {res}");

            Assert.False(string.IsNullOrWhiteSpace(res) && toolCallsBuffer.Count == 0);
        }
        
        [Fact]
        public async Task ChatManualFunctionCallTest()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲。你必须使用提供的工具来回答用户的问题，禁止自行猜测答案。当需要查询地址时使用Search工具，当需要查找书店时使用FindBookStore工具。"),
                new ChatMessage(ChatRole.User,"请帮我查一下南宁火车站的地址，然后帮我找那附近的书店。"),
            };
            var chatOptions = new VllmChatOptions
            {
                Tools = [AIFunctionFactory.Create(Search),AIFunctionFactory.Create(FindBookStore)],
                ThinkingEnabled = true
            };
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = cts.Token;

            var res = await _client.GetResponseAsync(messages, chatOptions, token);
            Assert.NotNull(res);

            _output.WriteLine($"Initial Response: {res.FinishReason}");
            foreach(var msg in res.Messages)
            {
                _output.WriteLine($"[Response Msg] {msg.Role}: {msg.Contents.Count} contents");
                foreach(var c in msg.Contents) _output.WriteLine($"  - {c.GetType().Name}: {c}");
            }
            
            int loopCount = 0;
            // 循环处理工具调用
            while(res.FinishReason == ChatFinishReason.ToolCalls && loopCount++ < 10)
            {
                // 1. 将返回的 Assistant 消息添加到历史
                messages.AddMessages(res);

                var functionCalls = res.Messages.SelectMany(m => m.Contents.OfType<FunctionCallContent>()).ToList();
                Assert.NotEmpty(functionCalls);

                _output.WriteLine($"Tool Calls: {functionCalls.Count}");

                // 2. 处理所有工具调用并添加 Tool 消息
                foreach (var functionCall in functionCalls)
                {
                    Assert.NotNull(functionCall);
                    string argsJson = JsonSerializer.Serialize(functionCall.Arguments);
                    _output.WriteLine($"Processing tool: {functionCall.Name} Args: {argsJson}");

                    var anwser = string.Empty;
                    if ("GetWeather" == functionCall.Name)
                    {
                        anwser = "30度，天气晴朗。";
                    }
                    else if ("FindBookStore" == functionCall.Name)
                    {
                        // 简单的参数解析，实际情况可能更复杂
                        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
                        
                        if(args != null && args.ContainsKey("dest"))
                        {
                            anwser = FindBookStore(args["dest"]);
                        }
                        else
                        {
                            anwser = "参数错误";
                        }
                    }
                    else if ("Search" == functionCall.Name)
                    {
                        anwser = "在青秀区方圆广场附近站前路1号。";
                    }

                    var functionResult = new FunctionResultContent(functionCall.CallId, anwser);
                    // Tool 消息必须单独添加，OpenAI 格式要求每个 Result 一个消息
                    messages.Add(new ChatMessage(ChatRole.Tool, [functionResult]));
                }
                
                // 调试输出消息历史
                _output.WriteLine("--- Manual Test Message History ---");
                for(int i=0; i<messages.Count; i++)
                {
                    var m = messages[i];
                    _output.WriteLine($"[{i}] [{m.Role}] {string.Join(", ", m.Contents.Select(c => c.ToString()))}");
                }
                _output.WriteLine("-----------------------------------");

                // 3. 获取下一轮响应
                res = await _client.GetResponseAsync(messages, chatOptions, token);
                _output.WriteLine($"Next Round Response: {res.FinishReason}");
            }

            Assert.NotNull(res);
            string answerText = string.Empty;
            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
                _output.WriteLine($"Response: {reasoningResponse.Text}");
            }
            else
            {
                _output.WriteLine($"Response: {res.Text}");
            }
            answerText = res.Text;
            Assert.False(string.IsNullOrWhiteSpace(answerText));
            // 验证最终回答是否包含相关信息
            Assert.True(answerText.Contains("爱民书店") || answerText.Contains("100米") || answerText.Contains("方圆广场"), "Reply should contain location info");
        }
    }
}
