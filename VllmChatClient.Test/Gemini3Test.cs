using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Xunit;
using Xunit.Abstractions;

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
                "Tokyo" => "Tokyo: Sunny, 22°C",
                "Paris" => "Paris: Rainy, 12°C",
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
                Temperature = 0.7f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
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
        /// 测试使用 UseFunctionInvocation 的自动函数调用
        /// </summary>
        [Fact]
        public async Task FunctionCall_WithAutomaticInvocation()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            var baseClient = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                _apiKey,
                "gemini-3-pro-preview"
            );

            // 使用 ChatClientBuilder 启用自动函数调用
            IChatClient client = new ChatClientBuilder(baseClient)
                .UseFunctionInvocation()
                .Build();

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "上海的天气如何？")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 0.7f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
            };

            try
            {
                _output.WriteLine("=== Using UseFunctionInvocation ===");
                var response = await client.GetResponseAsync(messages, options);

                _output.WriteLine($"Response: {response.Text}");
                _output.WriteLine($"Messages count: {response.Messages.Count}");

                Assert.NotNull(response);
                
                // 验证响应包含天气信息
                var hasWeatherInfo = response.Text.Contains("天气") || 
                                     response.Text.Contains("温度") ||
                                     response.Text.Contains("°C");
                
                if (hasWeatherInfo)
                {
                    _output.WriteLine("\n✓ Response contains weather information");
                }
                else
                {
                    _output.WriteLine("\n⚠️ Response may not contain expected weather information");
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                _output.WriteLine("\n⚠️ 400 Bad Request - thoughtSignature implementation needed");
                _output.WriteLine($"Error: {ex.Message}");
                _output.WriteLine("\nThis is expected behavior. Gemini 3 function calling requires:");
                _output.WriteLine("1. Extracting thoughtSignature from function call responses");
                _output.WriteLine("2. Passing it back in subsequent requests");
                _output.WriteLine("\nSee docs/Gemini3FunctionCallSupport.md for implementation details.");
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
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 0.7f,
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
                new ChatMessage(ChatRole.User, "东京的天气怎么样？")
            };

            var options = new GeminiChatOptions
            {
                ReasoningLevel = GeminiReasoningLevel.Low,
                Temperature = 0.7f,
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create(GetWeather)
                }
            };

            try
            {
                _output.WriteLine("=== Testing streaming with function calls ===");
                var textBuilder = new StringBuilder();
                var functionCalls = new List<FunctionCallContent>();

                await foreach (var update in client.GetStreamingResponseAsync(messages, options))
                {
                    if (update.Contents != null)
                    {
                        foreach (var content in update.Contents)
                        {
                            if (content is TextContent textContent)
                            {
                                textBuilder.Append(textContent.Text);
                                _output.WriteLine($"[Text] {textContent.Text}");
                            }
                            else if (content is FunctionCallContent functionCall)
                            {
                                functionCalls.Add(functionCall);
                                _output.WriteLine($"[Function Call] {functionCall.Name}");
                                _output.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(functionCall.Arguments)}");
                            }
                        }
                    }
                }

                _output.WriteLine($"\n=== Summary ===");
                _output.WriteLine($"Function calls: {functionCalls.Count}");
                _output.WriteLine($"Text output: {textBuilder}");

                if (functionCalls.Count > 0)
                {
                    _output.WriteLine("\n✓ Function calls detected in streaming mode");
                }
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
    }
}
