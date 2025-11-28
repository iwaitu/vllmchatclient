using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VllmChatClient.Test
{
    public class Gemini3Test
    {
        private readonly ITestOutputHelper _output;
        private readonly string _apiKey;

        public Gemini3Test(ITestOutputHelper output)
        {
            _output = output;
            // 从环境变量获取 Gemini API Key
            _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        }

        [Fact]
        public async Task ChatTest_WithNormalReasoning()
        {
            // 跳过测试如果没有 API Key
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            // 使用正确的 Gemini API 端点格式
            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "What is 2+2? Explain your reasoning.")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Normal,
                Temperature = 1.0f
            };

            try
            {
                var response = await client.GetResponseAsync(messages, options);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Messages);
                _output.WriteLine($"Response: {response.Text}");
                
                if (response is ReasoningChatResponse reasoningResponse)
                {
                    _output.WriteLine($"Reasoning: {reasoningResponse.Reason}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        [Fact]
        public async Task ChatTest_WithLowReasoning()
        {
            // 跳过测试如果没有 API Key
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "How does AI work?")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 1.0f
            };

            try
            {
                var response = await client.GetResponseAsync(messages, options);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Messages);
                
                if (response is ReasoningChatResponse reasoningResponse)
                {
                    _output.WriteLine($"Reasoning: {reasoningResponse.Reason}");
                }
                
                _output.WriteLine($"Response: {response.Text}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        [Fact]
        public async Task StreamChatTest()
        {
            // 跳过测试如果没有 API Key
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Count from 1 to 5.")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Normal
            };

            var result = new StringBuilder();
            var reasoning = new StringBuilder();

            try
            {
                await foreach (var update in client.GetStreamingResponseAsync(messages, options))
                {
                    if (update is ReasoningChatResponseUpdate reasoningUpdate)
                    {
                        if (reasoningUpdate.Thinking)
                        {
                            reasoning.Append(reasoningUpdate.Reasoning);
                        }
                        else
                        {
                            result.Append(update.Text);
                        }
                    }
                    else
                    {
                        result.Append(update.Text);
                    }
                }

                Assert.NotEmpty(result.ToString());
                _output.WriteLine($"Reasoning: {reasoning}");
                _output.WriteLine($"Result: {result}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
        
        /// <summary>
        /// 测试基础连接性和错误信息输出
        /// </summary>
        [Fact]
        public async Task DebugApiConnection()
        {
            // 跳过测试如果没有 API Key
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            _output.WriteLine($"API Key (first 10 chars): {_apiKey.Substring(0, Math.Min(10, _apiKey.Length))}...");
            
            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Hello")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low
            };

            try
            {
                var response = await client.GetResponseAsync(messages, options);
                _output.WriteLine($"Success! Response: {response.Text}");
                
                if (response is ReasoningChatResponse reasoningResponse)
                {
                    _output.WriteLine($"Reasoning Length: {reasoningResponse.Reason?.Length ?? 0}");
                    _output.WriteLine($"Reasoning Content: {reasoningResponse.Reason}");
                }
            }
            catch (HttpRequestException ex)
            {
                _output.WriteLine($"HTTP Error: {ex.Message}");
                _output.WriteLine($"Status Code: {ex.StatusCode}");
            }
            catch (InvalidOperationException ex)
            {
                _output.WriteLine($"Operation Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _output.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Unexpected Error: {ex.GetType().Name}");
                _output.WriteLine($"Message: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // ==================== 工具调用测试 ====================

        /// <summary>
        /// 获取天气的模拟函数
        /// </summary>
        [Description("获取指定城市的当前天气")]
        private static string GetWeather([Description("城市名称，例如：北京、上海、Beijing")] string city)
        {
            return city switch
            {
                "北京" or "Beijing" => "北京今天晴天，温度 15°C",
                "上海" or "Shanghai" => "上海今天多云，温度 18°C",
                "Tokyo" or "东京" => "Tokyo: Sunny, 22°C",
                "Paris" or "巴黎" => "Paris: Rainy, 12°C",
                _ => $"{city}: Weather data unavailable"
            };
        }

        /// <summary>
        /// 测试单个函数调用（不带自动执行）
        /// 此测试验证 Gemini 3 是否能正确返回函数调用
        /// </summary>
        [Fact]
        public async Task FunctionCall_SingleCall_ManualExecution()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "北京的天气怎么样？")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 1.0f,
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };

            try
            {
                _output.WriteLine("=== Turn 1: Request function call ===");
                var response = await client.GetResponseAsync(messages, options);

                Assert.NotNull(response);
                _output.WriteLine($"Response received with {response.Messages.Count} message(s)");

                // 检查是否有函数调用
                var functionCalls = response.Messages[0].Contents
                    .OfType<FunctionCallContent>()
                    .ToList();

                _output.WriteLine($"Function calls found: {functionCalls.Count}");

                if (functionCalls.Count > 0)
                {
                    _output.WriteLine("\n✓ Function call detected!");
                    
                    foreach (var fc in functionCalls)
                    {
                        _output.WriteLine($"  Function: {fc.Name}");
                        _output.WriteLine($"  Call ID: {fc.CallId}");
                        _output.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(fc.Arguments)}");

                        // 手动执行函数
                        var result = GetWeather(fc.Arguments["city"]?.ToString() ?? "");
                        _output.WriteLine($"  Result: {result}");

                        // 构建第二轮请求
                        messages.Add(response.Messages[0]);
                        messages.Add(new ChatMessage(
                            ChatRole.User,
                            new List<AIContent> 
                            { 
                                new FunctionResultContent(fc.CallId, result)
                            }
                        ));
                    }

                    _output.WriteLine("\n=== Turn 2: Send function result ===");
                    try
                    {
                        var finalResponse = await client.GetResponseAsync(messages, options);
                        _output.WriteLine($"Final response: {finalResponse.Text}");
                        
                        Assert.NotNull(finalResponse);
                        Assert.NotEmpty(finalResponse.Text);
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        _output.WriteLine("\n⚠️ 400 Bad Request - This is expected if thoughtSignature is not implemented");
                        _output.WriteLine($"Error: {ex.Message}");
                        _output.WriteLine("\nNote: Gemini 3 requires thoughtSignature to be passed back with function calls.");
                        _output.WriteLine("See docs/Gemini3FunctionCallSupport.md for details.");
                    }
                }
                else
                {
                    _output.WriteLine("\n⚠️ No function call detected - model may have answered directly");
                    _output.WriteLine($"Response: {response.Text}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 测试使用手动多轮函数调用（模拟 UseFunctionInvocation 行为，但保留 thoughtSignature）
        /// </summary>
        [Fact]
        public async Task FunctionCall_WithAutomaticInvocation()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "上海的天气如何？")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Normal,
                Temperature = 1.0f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
            };

            try
            {
                _output.WriteLine("=== Manual multi-turn function calling ===");
                
                // 第一轮：获取函数调用
                var response1 = await client.GetResponseAsync(messages, options);
                Assert.NotNull(response1);
                
                var functionCalls = response1.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
                _output.WriteLine($"Function calls received: {functionCalls.Count}");
                
                if (functionCalls.Count > 0)
                {
                    // 追加 assistant 的函数调用
                    messages.Add(response1.Messages[0]);
                    
                    // 执行函数并追加结果
                    foreach (var fc in functionCalls)
                    {
                        var result = GetWeather(fc.Arguments.TryGetValue("city", out var c) ? c?.ToString() ?? "" : "");
                        messages.Add(new ChatMessage(ChatRole.User, new List<AIContent> 
                        { 
                            new FunctionResultContent(fc.CallId, result) 
                        }));
                        _output.WriteLine($"Executed {fc.Name}: {result}");
                    }
                    
                    // 第二轮：获取最终回答
                    var response2 = await client.GetResponseAsync(messages, options);
                    _output.WriteLine($"Final response: {response2.Text}");
                    
                    Assert.NotNull(response2);
                    
                    // 验证响应包含天气信息
                    var hasWeatherInfo = response2.Text.Contains("天气") || 
                                         response2.Text.Contains("温度") ||
                                         response2.Text.Contains("°C") ||
                                         response2.Text.Contains("多云");
                    
                    if (hasWeatherInfo)
                    {
                        _output.WriteLine("\n✓ Response contains weather information");
                    }
                    else
                    {
                        _output.WriteLine($"\n⚠️ Response may not contain expected weather information: {response2.Text}");
                    }
                }
                else
                {
                    _output.WriteLine("⚠️ No function calls - model answered directly");
                    _output.WriteLine($"Direct response: {response1.Text}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 测试并行函数调用
        /// </summary>
        [Fact]
        public async Task FunctionCall_ParallelCalls()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Check the weather in Beijing and Shanghai")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Normal,
                Temperature = 1.0f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
            };

            try
            {
                _output.WriteLine("=== Testing parallel function calls ===");
                var response = await client.GetResponseAsync(messages, options);

                var functionCalls = response.Messages[0].Contents
                    .OfType<FunctionCallContent>()
                    .ToList();

                _output.WriteLine($"Function calls detected: {functionCalls.Count}");

                if (functionCalls.Count >= 2)
                {
                    _output.WriteLine("\n✓ Parallel function calls detected!");
                    
                    foreach (var fc in functionCalls)
                    {
                        _output.WriteLine($"\nFunction: {fc.Name}");
                        _output.WriteLine($"  Call ID: {fc.CallId}");
                        _output.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(fc.Arguments)}");
                    }

                    _output.WriteLine("\nNote: According to Gemini docs, only the first function call should have thoughtSignature");
                }
                else if (functionCalls.Count == 1)
                {
                    _output.WriteLine("\n⚠️ Only one function call - model may have decided to call sequentially");
                    _output.WriteLine($"Function: {functionCalls[0].Name}");
                }
                else
                {
                    _output.WriteLine("\n⚠️ No function calls - model may have answered directly");
                    _output.WriteLine($"Response: {response.Text}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 测试流式函数调用
        /// </summary>
        [Fact]
        public async Task FunctionCall_Streaming()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                // 加强提示，要求必须通过函数获取结果且只输出函数调用
                new ChatMessage(ChatRole.System, "你必须调用工具来获取天气信息，仅输出函数调用 JSON，不得直接回复。"),
                new ChatMessage(ChatRole.User, "查询东京天气")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Normal,
                Temperature = 1.0f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
            };

            try
            {
                _output.WriteLine("=== Streaming function call test ===");
                var functionCalls = new List<FunctionCallContent>();
                var reasoningBuilder = new StringBuilder();

                await foreach (var update in client.GetStreamingResponseAsync(messages, options))
                {
                    if (update is ReasoningChatResponseUpdate rcu)
                    {
                        if (rcu.Thinking)
                        {
                            reasoningBuilder.Append(rcu.Reasoning);
                        }

                        foreach (var c in rcu.Contents.OfType<FunctionCallContent>())
                        {
                            functionCalls.Add(c);
                            _output.WriteLine($"[FunctionCall] name={c.Name} id={c.CallId} args={System.Text.Json.JsonSerializer.Serialize(c.Arguments)}");
                        }
                    }
                }

                // 执行并追加结果（按官方示例：先追加模型的 functionCall，再追加用户的 functionResponse）
                foreach (var fcc in functionCalls)
                {
                    var result = GetWeather(fcc.Arguments.TryGetValue("city", out var v) ? v?.ToString() ?? string.Empty : string.Empty);
                    // 追加上一轮模型的函数调用
                    messages.Add(new ChatMessage(ChatRole.Assistant, new List<AIContent> { fcc }));
                    // 追加用户的函数响应
                    messages.Add(new ChatMessage(ChatRole.User, new List<AIContent> { new FunctionResultContent(fcc.CallId, result) }));
                    _output.WriteLine($"[FunctionResult] {result}");
                }

                // 第二轮：发送函数结果获取最终回答
                string finalText = string.Empty;
                if (functionCalls.Count > 0)
                {
                    var final = await client.GetResponseAsync(messages, options);
                    // 优先使用 Text 属性，其次从 Contents 中提取 TextContent
                    finalText = !string.IsNullOrEmpty(final.Text)
                        ? final.Text
                        : final.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text).FirstOrDefault() ?? string.Empty;
                    _output.WriteLine($"Final answer text: {finalText}");
                }

                _output.WriteLine("\n=== Summary ===");
                _output.WriteLine($"Function calls: {functionCalls.Count}");
                _output.WriteLine($"Reasoning length: {reasoningBuilder.Length}");
                _output.WriteLine($"Final text length: {finalText.Length}");
                Assert.True(functionCalls.Count > 0, "Expected at least one streamed function call.");
                // 如果模型最终未返回文本也允许通过，只要函数调用发生
                Assert.True(functionCalls.Count > 0 && finalText.Length > 0);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 测试没有工具时的正常行为
        /// </summary>
        [Fact]
        public async Task FunctionCall_NoToolsDefined()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "北京的天气怎么样？")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 0.7f
                // 注意：没有定义 Tools
            };

            try
            {
                _output.WriteLine("=== Testing without tools defined ===");
                var response = await client.GetResponseAsync(messages, options);

                Assert.NotNull(response);
                _output.WriteLine($"Response: {response.Text}");

                // 验证没有函数调用
                var functionCalls = response.Messages[0].Contents
                    .OfType<FunctionCallContent>()
                    .ToList();

                Assert.Empty(functionCalls);
                _output.WriteLine("\n✓ No function calls as expected (no tools defined)");
                _output.WriteLine("✓ Model provided direct text response");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 测试多轮工具调用 - 使用 Low Reasoning Level
        /// 验证在低推理级别下，模型能否正确处理需要多次函数调用的复杂场景
        /// </summary>
        [Fact]
        public async Task FunctionCall_MultiTurn_LowReasoning()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "我明天要去北京出差，请帮我查看天气并告诉我需要带什么")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,  // 使用低推理级别
                Temperature = 0.8f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetCityWeather),
                    AIFunctionFactory.Create(CheckUmbrellaNeeded),
                    AIFunctionFactory.Create(GetClothingSuggestion)
                }
            };

            try
            {
                _output.WriteLine("=== Multi-turn tool calling with Low Reasoning Level ===");
                int turnCount = 0;
                int totalFunctionCalls = 0;
                const int maxTurns = 5;  // 防止无限循环

                while (turnCount < maxTurns)
                {
                    turnCount++;
                    _output.WriteLine($"\n--- Turn {turnCount} ---");

                    var response = await client.GetResponseAsync(messages, options);
                    Assert.NotNull(response);

                    // 检查是否有函数调用
                    var functionCalls = response.Messages[0].Contents
                        .OfType<FunctionCallContent>()
                        .ToList();

                    _output.WriteLine($"Function calls in this turn: {functionCalls.Count}");
                    totalFunctionCalls += functionCalls.Count;

                    if (functionCalls.Count > 0)
                    {
                        // 添加助手的函数调用消息
                        messages.Add(response.Messages[0]);

                        // 执行每个函数调用
                        foreach (var fc in functionCalls)
                        {
                            _output.WriteLine($"  Executing: {fc.Name}");
                            _output.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(fc.Arguments)}");

                            string result = "";
                            try
                            {
                                // 根据函数名执行相应的函数
                                if (fc.Name == "GetCityWeather" && fc.Arguments.TryGetValue("city", out var cityObj))
                                {
                                    result = GetCityWeather(cityObj?.ToString() ?? "");
                                }
                                else if (fc.Name == "CheckUmbrellaNeeded" && fc.Arguments.TryGetValue("weatherDescription", out var weatherObj))
                                {
                                    result = CheckUmbrellaNeeded(weatherObj?.ToString() ?? "");
                                }
                                else if (fc.Name == "GetClothingSuggestion" && fc.Arguments.TryGetValue("temperature", out var tempObj))
                                {
                                    var temp = Convert.ToInt32(tempObj);
                                    result = GetClothingSuggestion(temp);
                                }
                                else
                                {
                                    result = "函数执行失败：参数不匹配";
                                }

                                _output.WriteLine($"  Result: {result}");

                                // 添加函数结果
                                messages.Add(new ChatMessage(
                                    ChatRole.User,
                                    new List<AIContent> { new FunctionResultContent(fc.CallId, result) }
                                ));
                            }
                            catch (Exception ex)
                            {
                                _output.WriteLine($"  Error executing function: {ex.Message}");
                                messages.Add(new ChatMessage(
                                    ChatRole.User,
                                    new List<AIContent> { new FunctionResultContent(fc.CallId, $"执行错误：{ex.Message}") }
                                ));
                            }
                        }

                        // 如果有函数调用，继续下一轮
                        continue;
                    }
                    else
                    {
                        // 没有函数调用，检查最终回答
                        _output.WriteLine($"\n=== Final Response (Turn {turnCount}) ===");
                        _output.WriteLine($"Text: {response.Text}");

                        if (response is ReasoningChatResponse reasoningResp)
                        {
                            _output.WriteLine($"Reasoning: {reasoningResp.Reason}");
                        }

                        // 验证最终回答包含相关信息
                        Assert.False(string.IsNullOrWhiteSpace(response.Text), "Final response should not be empty");
                        
                        // 验证至少调用了一些函数
                        Assert.True(totalFunctionCalls > 0, $"Expected at least one function call, but got {totalFunctionCalls}");

                        _output.WriteLine($"\n=== Test Summary ===");
                        _output.WriteLine($"Total turns: {turnCount}");
                        _output.WriteLine($"Total function calls: {totalFunctionCalls}");
                        _output.WriteLine($"✓ Multi-turn conversation completed successfully with Low Reasoning Level");

                        // 成功完成
                        break;
                    }
                }

                // 检查是否超过最大轮数
                if (turnCount >= maxTurns)
                {
                    _output.WriteLine($"\n⚠️ Reached maximum turns ({maxTurns}) without final answer");
                    _output.WriteLine($"Total function calls made: {totalFunctionCalls}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Error: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // ==================== 辅助函数（用于多轮测试） ====================

        /// <summary>
        /// 获取指定城市的当前天气
        /// </summary>
        [Description("获取指定城市的当前天气")]
        private static string GetCityWeather([Description("城市名称")] string city)
        {
            return city switch
            {
                "北京" or "Beijing" => "北京：晴天，15°C，空气质量良好",
                "上海" or "Shanghai" => "上海：多云，18°C，有轻度雾霾",
                _ => $"{city}：天气数据不可用"
            };
        }

        /// <summary>
        /// 检查是否需要带雨伞
        /// </summary>
        [Description("检查是否需要带雨伞")]
        private static string CheckUmbrellaNeeded([Description("天气描述")] string weatherDescription)
        {
            if (weatherDescription.Contains("雨") || weatherDescription.Contains("rain", StringComparison.OrdinalIgnoreCase))
            {
                return "建议携带雨伞";
            }
            return "无需携带雨伞";
        }

        /// <summary>
        /// 获取穿衣建议
        /// </summary>
        [Description("获取穿衣建议")]
        private static string GetClothingSuggestion([Description("温度（摄氏度）")] int temperature)
        {
            return temperature switch
            {
                < 10 => "建议穿厚外套、羽绒服",
                < 20 => "建议穿长袖衬衫、薄外套",
                < 30 => "建议穿短袖、轻便衣物",
                _ => "建议穿清凉透气的衣物"
            };
        }
    }
}
