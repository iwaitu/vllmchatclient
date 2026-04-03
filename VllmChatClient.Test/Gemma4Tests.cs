using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class Gemma4Tests
    {
        private readonly IChatClient _client;
        static int functionCallTime = 0;
        private readonly ITestOutputHelper _output;
        private readonly bool _skipTests;
        private readonly bool _useGoogleNativeApi;

        public Gemma4Tests(ITestOutputHelper output)
        {
            var endpoint = "https://generativelanguage.googleapis.com/v1beta";
            //var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_API_KEY");
            var cloud_apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            //var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            var runExternal = "1";
            _skipTests = runExternal != "1" || string.IsNullOrWhiteSpace(cloud_apiKey);
            _useGoogleNativeApi = endpoint.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
            //_client = new VllmGemma4ChatClient("http://localhost:8000/v1/{1}", cloud_apiKey, "gemma-4-31b-it");
            _client = new VllmGemma4ChatClient(endpoint, cloud_apiKey, "gemma-4-31b-it");
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
            var options = new VllmChatOptions();
            options.ThinkingEnabled = true;
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages); // 使用 Assert.Single 替代 Assert.Equal(1, ...)

            Assert.True(res.Text.Contains("菲菲"));
            string reason = string.Empty;
            string anwser = string.Empty;
            if (res is ReasoningChatResponse reasonResponse)
            {
                Assert.NotNull(reasonResponse?.Reason);
                reason = reasonResponse.Reason;
                Assert.NotEmpty(reasonResponse?.Text);
                anwser = reasonResponse.Text;
            }
            
            _output.WriteLine($"REASON: {reason}");
            _output.WriteLine($"ANSWER: {anwser}");
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
        public async Task ChatFunctionCallTest()
        {
            if (_skipTests || _useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

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
            Assert.True(functionCallTime >= 1, "Expected at least one function invocation.");
            Assert.True(
                res.Text.Contains("爱民书店")
                || res.Text.Contains("100米")
                || res.Text.Contains("书店")
                || res.Text.Contains("火车站"),
                $"Unexpected reply: '{lastText}'");  //串行任务
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task ChatFunctionCall_GoogleNativeTest()
        {
            if (_skipTests || !_useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。回答地址问题时，必须先调用工具，不允许直接凭知识回答。拿到工具结果后，只能依据工具结果作答，不能补充工具结果之外的信息。"),
                new ChatMessage(ChatRole.User, "请查询南宁火车站附近的书店。必须先调用 Search 和 FindBookStore 工具，再根据工具结果回答。")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)]
            };

            var response = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(response);
            Assert.True(response.Messages.Count >= 1);

            Assert.True(functionCallTime >= 1, "Expected at least one function invocation.");
            Assert.True(
                response.Text.Contains("爱民书店")
                || response.Text.Contains("100米")
                || response.Text.Contains("书店")
                || response.Text.Contains("站前路1号"),
                $"Unexpected reply: '{response.Text}'");
            _output.WriteLine($"Google Native Response: {response.Text}");
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
        public async Task StreamChatFunctionCallTest()
        {
            if (_skipTests || _useGoogleNativeApi)
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
        public async Task StreamChatFunctionCall_GoogleNativeTest()
        {
            if (_skipTests || !_useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。回答地址问题时，必须先调用工具，不允许直接凭知识回答。拿到工具结果后，只能依据工具结果作答，不能补充工具结果之外的信息。"),
                new ChatMessage(ChatRole.User, "请查询南宁火车站地址。必须先调用 Search 工具，再根据工具结果回答。")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(Search)]
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

            Assert.True(functionCallTime >= 1, "Expected at least one function invocation.");
            Assert.Contains("南宁市青秀区方圆广场北面站前路1号", res);
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
        }



        [Fact]
        public async Task StreamChatManualFunctionCallTest()
        {
            if (_skipTests || _useGoogleNativeApi)
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
                if (update.FinishReason == ChatFinishReason.ToolCalls)
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
                        if (fc.Name == "GetWeather")
                        {
                            var result = GetWeather();
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(fc.CallId, result)]));
                            continue;
                        }
                        else if (fc.Name == "Search")
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

            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            _output.WriteLine("REASON:{0}", reason);
            _output.WriteLine("RESULT:{0}", res);
        }

        [Fact]
        public async Task StreamChatManualFunctionCall_GoogleNativeTest()
        {
            if (_skipTests || !_useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。回答地址问题时，必须先调用工具，不允许直接凭知识回答。拿到工具结果后，只能依据工具结果作答，不能补充工具结果之外的信息。"),
                new ChatMessage(ChatRole.User, "请查询南宁火车站地址。必须先调用 Search 工具，再根据工具结果回答。")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(Search)]
            };

            string res = string.Empty;
            string reason = string.Empty;
            ChatResponseUpdate? lastUpdate = null;

            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                lastUpdate = update;
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

                if (update.FinishReason == ChatFinishReason.ToolCalls || update.FinishReason == ChatFinishReason.Stop)
                {
                    break;
                }
            }

            int safeGuard = 0;
            while (lastUpdate?.FinishReason == ChatFinishReason.ToolCalls && safeGuard++ < 4)
            {
                foreach (var functionCall in lastUpdate.Contents.OfType<FunctionCallContent>())
                {
                    Assert.Equal("Search", functionCall.Name);
                    messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));
                    var argsJson = JsonSerializer.Serialize(
                        functionCall.Arguments,
                        new JsonSerializerOptions
                        {
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        });
                    var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
                    Assert.NotNull(args);
                    Assert.True(args.ContainsKey("question"));
                    messages.Add(new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent(functionCall.CallId, Search(args["question"]))]));
                }

                await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
                {
                    lastUpdate = update;
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

                    if (update.FinishReason == ChatFinishReason.ToolCalls || update.FinishReason == ChatFinishReason.Stop)
                    {
                        break;
                    }
                }
            }

            Assert.True(functionCallTime >= 1, "Expected Search to be executed at least once.");
            Assert.Contains("南宁市青秀区方圆广场北面站前路1号", res);
            _output.WriteLine("REASON:{0}", reason);
            _output.WriteLine("RESULT:{0}", res);
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
                if (update is ReasoningChatResponseUpdate reasonUpdate)
                {
                    if (reasonUpdate.Thinking)
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

        [Fact]
        public async Task ChatWithImageTest()
        {
            var userMessage = new ChatMessage(ChatRole.User, "详细描述图片的内容");

            var imageUrl = "https://ofasys-multimodal-wlcb-3-toshanghai.oss-accelerate.aliyuncs.com/wpf272043/keepme/image/receipt.png";
            using var http = new HttpClient();
            var response = await http.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            Assert.False(string.IsNullOrWhiteSpace(mediaType));
            Assert.StartsWith("image/", mediaType!, StringComparison.OrdinalIgnoreCase);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);

            userMessage.Contents.Add(new DataContent(bytes, mediaType!));

            var result = await _client.GetResponseAsync([userMessage], new ChatOptions());
            Assert.NotNull(result);
            Assert.NotNull(result.Messages);
            Assert.NotEmpty(result.Messages);
            Assert.False(string.IsNullOrWhiteSpace(result.Text));

            _output.WriteLine($"Response: {result.Text}");
        }

        [Fact]
        public async Task Nothinking_TestStreamJsonOuput()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "请仅输出单个json对象格式的问候语，不要输出任何解释、前后缀文本、markdown或 codeblock。")
            };

            var options = new VllmChatOptions
            {
                ThinkingEnabled = false,
                MaxOutputTokens = 1024,
            };

            string text = string.Empty;
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
                        text += reasoningUpdate.Text;
                    }
                }
                else
                {
                    text += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(string.IsNullOrWhiteSpace(reason), $"关闭思维链后流式输出不应返回 reasoning 内容: '{reason}'");

            var cleaned = Regex.Replace(text, "<think>.*?</think>\\n*", string.Empty, RegexOptions.Singleline).Trim();

            Assert.DoesNotContain("<think>", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("</think>", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("```", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("```json", cleaned, StringComparison.OrdinalIgnoreCase);

            var jsonText = TryExtractFirstJsonValue(cleaned);
            Assert.False(string.IsNullOrWhiteSpace(jsonText), $"未找到JSON片段: '{cleaned}'");

            var json = JsonDocument.Parse(jsonText!);
            Assert.NotNull(json);
            Assert.True(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, $"输出不是有效JSON对象或数组: '{jsonText}'");
            _output.WriteLine(cleaned);
        }

        [Fact]
        public async Task Nothinking_TestJsonOuput()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User, "请仅输出单个json对象格式的问候语，不要输出任何解释、前后缀文本、markdown或 codeblock。")
            };

            var options = new VllmChatOptions
            {
                ThinkingEnabled = false,
                MaxOutputTokens = 1024,
            };

            var response = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(response);
            Assert.Single(response.Messages);

            var text = response.Text;
            Assert.False(string.IsNullOrWhiteSpace(text));

            if (response is ReasoningChatResponse reasoningResponse)
            {
                Assert.True(string.IsNullOrWhiteSpace(reasoningResponse.Reason), $"关闭思维链后不应返回 reasoning 内容: '{reasoningResponse.Reason}'");
            }

            var cleaned = Regex.Replace(text, "<think>.*?</think>\\n*", string.Empty, RegexOptions.Singleline).Trim();

            Assert.DoesNotContain("<think>", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("</think>", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("```", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("```json", cleaned, StringComparison.OrdinalIgnoreCase);

            var jsonText = TryExtractFirstJsonValue(cleaned);
            Assert.False(string.IsNullOrWhiteSpace(jsonText), $"未找到JSON片段: '{cleaned}'");

            var json = JsonDocument.Parse(jsonText!);
            Assert.NotNull(json);
            Assert.True(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, $"输出不是有效JSON对象或数组: '{jsonText}'");
            _output.WriteLine(cleaned);
        }


        [Description("获取南宁的天气情况")]
        static string GetWeather() => "现在正在下雨。";


        [Description("Searh")]
        static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        private static string? TryExtractFirstJsonValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = text.Trim();

            try
            {
                using var _ = JsonDocument.Parse(text);
                return text;
            }
            catch (JsonException)
            {
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('{' or '['))
                {
                    continue;
                }

                int depth = 0;
                bool inString = false;
                bool escape = false;

                for (int j = i; j < text.Length; j++)
                {
                    var c = text[j];

                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = !inString;
                        continue;
                    }

                    if (inString)
                    {
                        continue;
                    }

                    if (c is '{' or '[')
                    {
                        depth++;
                    }
                    else if (c is '}' or ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var candidate = text[i..(j + 1)];
                            try
                            {
                                using var _ = JsonDocument.Parse(candidate);
                                return candidate;
                            }
                            catch (JsonException)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return null;
        }

        [Fact]
        public async Task ChatManualFunctionCallTest()
        {
            if (_skipTests || _useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲。"),
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
                messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));

                Assert.NotNull(functionCall);
                var anwser = string.Empty;
                if ("GetWeather" == functionCall.Name)
                {
                    anwser = "30度，天气晴朗。";
                }
                else
                {
                    anwser = "工具查询结果：南宁市青秀区方圆广场附近站前路1号。请严格根据这个地址回答。";
                }
                _output.WriteLine($"Function Call: {functionCall.Name}, Arguments: {JsonSerializer.Serialize(functionCall.Arguments)}");
                var functionResult = new FunctionResultContent(functionCall.CallId, anwser);
                _output.WriteLine($"Function Result: {functionResult.Result}");
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
            Assert.Contains("南宁市青秀区方圆广场附近站前路1号", answerText);
            _output.WriteLine($"Answer: {answerText}");
        }

        [Fact]
        public async Task ChatManualFunctionCall_GoogleNativeTest()
        {
            if (_skipTests || !_useGoogleNativeApi)
            {
                return;
            }

            functionCallTime = 0;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "你是一个智能助手，名字叫菲菲。回答地址问题时，必须先调用工具，不允许直接凭知识回答。拿到工具结果后，只能依据工具结果作答，不能补充工具结果之外的信息。"),
                new ChatMessage(ChatRole.User, "请查询南宁火车站地址。必须先调用 Search 工具，再根据工具结果回答。")
            };

            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(Search)]
            };

            var response = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(response);
            Assert.Single(response.Messages);

            var functionCalls = response.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
            Assert.NotEmpty(functionCalls);

            foreach (var functionCall in functionCalls)
            {
                Assert.Equal("Search", functionCall.Name);
                messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));

                var functionResult = new FunctionResultContent(functionCall.CallId, "南宁市青秀区方圆广场北面站前路1号。");
                messages.Add(new ChatMessage(ChatRole.Tool, [functionResult]));
            }

            var finalResponse = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(finalResponse);
            Assert.Single(finalResponse.Messages);
            Assert.Contains("南宁市青秀区方圆广场北面站前路1号", finalResponse.Text);
            _output.WriteLine($"Google Native Answer: {finalResponse.Text}");
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
                new ChatMessage(ChatRole.User,"请输出json格式的问候语，不要使用代码块")
            };
            var options = new ChatOptions
            {
                MaxOutputTokens = 1024,
                Temperature = 0.9f,
            };
            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            var textContent = res.Messages[0].Text;
            Assert.NotNull(textContent);

            // 验证不包含代码块标记
            Assert.All(textContent.Split('\n'), line =>
            {
                Assert.DoesNotContain("```", line);
                Assert.DoesNotContain("```json", line);
            });

            // 从文本中提取 JSON 片段并验证
            var jsonMatch = Regex.Match(textContent, @"(\{[^}]*\}|\[[^\]]*\])", RegexOptions.Singleline);
            Assert.True(jsonMatch.Success, $"未找到JSON片段: '{textContent}'");
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
