using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class Qwen36PlusAnthropicTests
    {
        private const string MODEL = "qwen3.6-plus";

        private static int functionCallTime = 0;

        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;
        private readonly VllmChatOptions _chatOptions;
        private readonly bool _skipTests;

        public Qwen36PlusAnthropicTests(ITestOutputHelper output)
        {
            _output = output;

            var cloudApiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            var runExternal = "1";
            _skipTests = runExternal != "1" || string.IsNullOrWhiteSpace(cloudApiKey);

            _client = new VllmQwen3NextChatClient(
                "https://dashscope.aliyuncs.com/apps/anthropic",
                cloudApiKey,
                MODEL,
                null,
                VllmApiMode.AnthropicMessages);

            _chatOptions = new VllmChatOptions
            {
                ThinkingEnabled = true,
                MaxOutputTokens = 3000,
            };
        }

        [Description("获取南宁的天气情况")]
        private static string GetWeather() => "现在正在下雨。";

        [Description("Search")]
        private static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [Description("搜索周边的书店")]
        private static string FindBookStore([Description("需要搜索的具体地址/门牌号")] string dest)
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲 "),
                new(ChatRole.User, "你是谁？")
            };

            var res = await _client.GetResponseAsync(messages, _chatOptions);

            Assert.NotNull(res);
            Assert.Single(res.Messages);
            Assert.Contains("菲菲", res.Text);
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, "你是谁？")
            };

            var answer = string.Empty;
            var reason = string.Empty;

            await foreach (var update in _client.GetStreamingResponseAsync(messages, _chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        answer += reasoningUpdate.Text;
                    }
                }
                else
                {
                    answer += update.Text;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(answer));
            Assert.Contains("菲菲", answer);
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {answer}");
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲。"),
                new(ChatRole.User, "南宁火车站在哪里？我想到那附近去买书。")
            };

            _chatOptions.Tools =
            [
                AIFunctionFactory.Create(GetWeather),
                AIFunctionFactory.Create(Search),
                AIFunctionFactory.Create(FindBookStore)
            ];
            _chatOptions.Temperature = 0.2f;

            var res = await client.GetResponseAsync(messages, _chatOptions);

            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), $"Unexpected reply: '{res.Text}'");
            _output.WriteLine($"Response: {res.Text}");
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };

            _chatOptions.Tools =
            [
                AIFunctionFactory.Create(GetWeather),
                AIFunctionFactory.Create(Search),
                AIFunctionFactory.Create(FindBookStore)
            ];

            var answer = string.Empty;
            var reason = string.Empty;
            UsageDetails? usage = null;

            await foreach (var update in client.GetStreamingResponseAsync(messages, _chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        answer += reasoningUpdate.Text;
                    }
                }
                else
                {
                    answer += update.Text;
                }

                if (update is UsageChatResponseUpdate usageUpdate)
                {
                    usage ??= usageUpdate.Usage;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(answer));
            Assert.True(answer.Contains("下雨") || answer.Contains("雨"), $"Unexpected reply: '{answer}'");
            Assert.NotNull(usage);
            Assert.True(usage!.InputTokenCount > 0, $"Unexpected input tokens: {usage.InputTokenCount}");
            Assert.True(usage.OutputTokenCount > 0, $"Unexpected output tokens: {usage.OutputTokenCount}");
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {answer}");
            _output.WriteLine($"Usage: input={usage.InputTokenCount}, output={usage.OutputTokenCount}, total={usage.TotalTokenCount}");
        }

        [Fact]
        public async Task StreamChatManualFunctionCallTest()
        {
            if (_skipTests)
            {
                return;
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };

            var options = new VllmChatOptions
            {
                ThinkingEnabled = true,
                MaxOutputTokens = 3000,
                Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)]
            };

            var answer = string.Empty;
            var reason = string.Empty;
            var handledToolCall = false;

            await foreach (var update in _client.GetStreamingResponseAsync(messages, options))
            {
                if (update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    handledToolCall = true;
                    foreach (var functionCall in update.Contents.OfType<FunctionCallContent>())
                    {
                        messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));

                        var json = JsonSerializer.Serialize(
                            functionCall.Arguments,
                            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

                        if (functionCall.Name == nameof(GetWeather))
                        {
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(functionCall.CallId, GetWeather())]));
                        }
                        else if (functionCall.Name == nameof(Search))
                        {
                            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                            Assert.NotNull(args);
                            Assert.True(args.ContainsKey("question"));
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(functionCall.CallId, Search(args["question"]))]));
                        }
                    }
                }
                else if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        answer += reasoningUpdate.Text;
                    }
                }
                else
                {
                    answer += update.Text;
                }
            }

            if (handledToolCall && string.IsNullOrWhiteSpace(answer))
            {
                options.Tools = null;

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
                            answer += reasoningUpdate.Text;
                        }
                    }
                    else
                    {
                        answer += update.Text;
                    }
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(answer));
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {answer}");
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, "请输出json格式的问候语,json 中必须包含 name 属性,不要输出代码块，例如：{\"name\":\"菲菲\"}")
            };

            var res = await _client.GetResponseAsync(messages, _chatOptions);

            Assert.NotNull(res);
            Assert.Single(res.Messages);
            AssertValidJsonFragment(res.Text);
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task TestJsonSchemaOutput()
        {
            if (_skipTests)
            {
                return;
            }

            var schema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" },
                    "greeting": { "type": "string" }
                  },
                  "required": ["name", "greeting"],
                  "additionalProperties": false
                }
                """).RootElement.Clone();

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, $"请严格按以下 JSON schema 返回 JSON 对象。输出必须且只能包含 name 和 greeting 两个字符串字段，其中 name 必须是“菲菲”，greeting 必须是一句问候语。不要输出代码块，也不要输出 JSON 之外的任何文字。\n\nJSON Schema:\n{schema.GetRawText()}")
            };

            var res = await _client.GetResponseAsync(messages, _chatOptions);

            Assert.NotNull(res);
            Assert.Single(res.Messages);
            Assert.DoesNotContain("```", res.Text);

            using var json = JsonDocument.Parse(res.Text.Trim());
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.True(json.RootElement.TryGetProperty("name", out var name));
            Assert.True(json.RootElement.TryGetProperty("greeting", out var greeting));
            Assert.False(string.IsNullOrWhiteSpace(name.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(greeting.GetString()));
            _output.WriteLine($"Response: {res.Text}");
        }

        [Fact]
        public async Task StreamFunctionCallThenJsonSchemaOutputTest()
        {
            if (_skipTests)
            {
                return;
            }

            functionCallTime = 0;

            var schema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "address": { "type": "string" },
                    "summary": { "type": "string" }
                  },
                  "required": ["address", "summary"],
                  "additionalProperties": false
                }
                """).RootElement.Clone();

            var options = new VllmChatOptions
            {
                ThinkingEnabled = true,
                MaxOutputTokens = 3000,
                Temperature = 0.2f,
                Tools = [AIFunctionFactory.Create(Search)],
            };

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲。涉及地址查询时必须先调用 Search 工具获取结果，然后严格按指定 schema 返回 JSON。不要输出代码块，也不要输出 JSON 之外的任何文字。"),
                new(ChatRole.User, $"请查询南宁火车站在哪里，并严格按以下 JSON schema 返回。summary 里请简要说明这是通过工具查询到的结果。\n\nJSON Schema:\n{schema.GetRawText()}")
            };

            var answer = string.Empty;
            var reason = string.Empty;
            UsageDetails? usage = null;

            await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            {
                if (update is ReasoningChatResponseUpdate reasoningUpdate)
                {
                    if (reasoningUpdate.Thinking)
                    {
                        reason += reasoningUpdate.Text;
                    }
                    else
                    {
                        answer += reasoningUpdate.Text;
                    }
                }
                else
                {
                    answer += update.Text;
                }

                if (update is UsageChatResponseUpdate usageUpdate)
                {
                    usage ??= usageUpdate.Usage;
                }
            }

            Assert.True(functionCallTime > 0, "Expected Search tool to be invoked at least once.");
            Assert.False(string.IsNullOrWhiteSpace(answer));
            Assert.DoesNotContain("```", answer);

            using var json = JsonDocument.Parse(answer.Trim());
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.Contains("南宁市青秀区方圆广场北面站前路1号", json.RootElement.GetProperty("address").GetString());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("summary").GetString()));
            Assert.NotNull(usage);
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {answer}");
        }

        private static void AssertValidJsonFragment(string text)
        {
            Assert.DoesNotContain("```", text);

            var jsonMatch = Regex.Match(text, @"(\{[^}]*\}|\[[^\]]*\])", RegexOptions.Singleline);
            Assert.True(jsonMatch.Success, $"未找到JSON片段: '{text}'");

            using var json = JsonDocument.Parse(jsonMatch.Value);
            Assert.NotNull(json);
        }
    }
}
