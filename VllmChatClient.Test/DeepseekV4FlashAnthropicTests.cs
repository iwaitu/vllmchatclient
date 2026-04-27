using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class DeepseekV4FlashAnthropicTests
    {
        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;
        private readonly VllmChatOptions _chatOptions;
        private readonly bool _skipTests;
        private const string MODEL = "deepseek-v4-flash";
        static int functionCallTime = 0;

        [Description("获取南宁的天气情况")]
        static string GetWeather() => "现在正在下雨。";

        [Description("搜索地点地址")]
        static string Search([Description("需要搜索的问题")] string question)
        {
            functionCallTime += 1;
            return "南宁市青秀区方圆广场北面站前路1号。";
        }

        [Description("搜索周边的书店")]
        static string FindBookStore([Description("需要搜索的具体地址/门牌号")] string dest)
        {
            functionCallTime += 1;
            return "附近100米有一家爱民书店。";
        }

        public DeepseekV4FlashAnthropicTests(ITestOutputHelper output)
        {
            _output = output;
            var cloudApiKey = Environment.GetEnvironmentVariable("VLLM_DEEPSEEK_API_KEY");
            var runExternal = "1";
            _skipTests = runExternal != "1" || string.IsNullOrWhiteSpace(cloudApiKey);
            _client = new VllmDeepseekV3ChatClient(
                "https://api.deepseek.com/anthropic",
                cloudApiKey,
                MODEL,
                null,
                VllmApiMode.AnthropicMessages);
            _chatOptions = new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 3000 };
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, "你是谁？")
            };

            var res = await _client.GetResponseAsync(messages, _chatOptions);
            Assert.NotNull(res);
            Assert.Single(res.Messages);
            Assert.Contains("菲菲", res.Text);

            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }

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

            string reason = string.Empty;
            string answer = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, _chatOptions))
            {
                if (update is ReasoningChatResponseUpdate reasoningMessage)
                {
                    if (reasoningMessage.Thinking)
                    {
                        reason += reasoningMessage.Text;
                    }
                    else
                    {
                        answer += reasoningMessage.Text;
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };

            _chatOptions.Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)];
            _chatOptions.Temperature = 0.2f;

            var res = await client.GetResponseAsync(messages, _chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count >= 1);
            Assert.True(res.Text.Contains("下雨", StringComparison.Ordinal) || res.Text.Contains("雨", StringComparison.Ordinal), $"Unexpected reply: '{res.Text}'");

            if (res is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }

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

            _chatOptions.Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)];
            string res = string.Empty;
            string reason = string.Empty;
            UsageDetails? usage = null;

            await foreach (var update in client.GetStreamingResponseAsync(messages, _chatOptions))
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

                if (update is UsageChatResponseUpdate usageUpdate)
                {
                    usage ??= usageUpdate.Usage;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.True(res.Contains("下雨", StringComparison.Ordinal) || res.Contains("雨", StringComparison.Ordinal), $"Unexpected reply: '{res}'");
            Assert.NotNull(usage);
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
            _output.WriteLine($"Usage: input={usage!.InputTokenCount}, output={usage.OutputTokenCount}, total={usage.TotalTokenCount}");
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲。你必须使用提供的工具来回答用户的问题，禁止自行猜测答案。调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new(ChatRole.User, "南宁火车站在哪里？我出门需要带伞吗？")
            };
            _chatOptions.Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)];
            string res = string.Empty;
            string reason = string.Empty;
            var toolCalls = new List<FunctionCallContent>();

            await foreach (var update in _client.GetStreamingResponseAsync(messages, _chatOptions))
            {
                toolCalls.AddRange(update.Contents.OfType<FunctionCallContent>());

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

            Assert.NotEmpty(toolCalls);
            messages.Add(new ChatMessage(ChatRole.Assistant, toolCalls.Cast<AIContent>().ToList()));

            foreach (var fc in toolCalls)
            {
                string json = JsonSerializer.Serialize(
                    fc.Arguments,
                    new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                if (fc.Name == "GetWeather")
                {
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(fc.CallId, GetWeather())]));
                }
                else if (fc.Name == "Search")
                {
                    var args = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    Assert.NotNull(args);
                    Assert.True(args.ContainsKey("question"));
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(fc.CallId, Search(args["question"]))]));
                }
            }

            res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages, _chatOptions))
            {
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

            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.True(res.Contains("下雨", StringComparison.Ordinal) || res.Contains("雨", StringComparison.Ordinal), $"Unexpected reply: '{res}'");
            _output.WriteLine($"Reason: {reason}");
            _output.WriteLine($"Response: {res}");
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
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲，调用工具时仅能输出工具调用内容，不能输出其他文本。"),
                new(ChatRole.User, "南宁火车站在哪里？")
            };

            _chatOptions.Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)];
            var res = await _client.GetResponseAsync(messages, _chatOptions);
            Assert.NotNull(res);
            Assert.Single(res.Messages);

            var functionCalls = res.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
            Assert.NotEmpty(functionCalls);

            foreach (var functionCall in functionCalls)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));
                var answer = functionCall.Name == "GetWeather"
                    ? "30度，天气晴朗。"
                    : "在青秀区方圆广场附近站前路1号。";
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(functionCall.CallId, answer)]));
            }

            var result = await _client.GetResponseAsync(messages, _chatOptions);
            Assert.NotNull(result);
            Assert.Single(result.Messages);
            Assert.False(string.IsNullOrWhiteSpace(result.Text));

            if (result is ReasoningChatResponse reasoningResponse)
            {
                _output.WriteLine($"Reason: {reasoningResponse.Reason}");
            }

            _output.WriteLine($"Response: {result.Text}");
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

            var options = new VllmChatOptions
            {
                ThinkingEnabled = _chatOptions.ThinkingEnabled,
                MaxOutputTokens = 3000,
            };

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User, $"请严格按以下 JSON schema 返回 JSON 对象。输出必须且只能包含 name 和 greeting 两个字符串字段，其中 name 必须是“菲菲”，greeting 必须是一句问候语。不要输出代码块，也不要输出 JSON 之外的任何文字。\n\nJSON Schema:\n{schema.GetRawText()}")
            };

            var res = await _client.GetResponseAsync(messages, options);
            Assert.NotNull(res);
            Assert.Single(res.Messages);

            _output.WriteLine($"Response: {res.Text}");

            var textContent = res.Text.Trim();
            Assert.DoesNotContain("```", textContent);

            using var json = JsonDocument.Parse(textContent);
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.True(json.RootElement.TryGetProperty("name", out var name));
            Assert.True(json.RootElement.TryGetProperty("greeting", out var greeting));
            Assert.False(string.IsNullOrWhiteSpace(name.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(greeting.GetString()));
        }

        [Fact]
        public async Task ExtractTags()
        {
            if (_skipTests)
            {
                return;
            }

            const string text = "不动产登记资料查询，即查档业务，包括查询房屋、土地、车库车位等不动产登记结果，以及复制房屋、土地、车库车位等不动产登记原始资料。\n";
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, $"请为以下文本提取3个最相关的标签。用json格式返回，不要输出代码块。\n\n文本:{text}")
            };

            var res = await _client.GetResponseAsync(messages, _chatOptions);
            Assert.NotNull(res);
            var match = Regex.Match(res.Text, @"\s*(\{.*?\}|\[.*?\])\s*", RegexOptions.Singleline);
            Assert.True(match.Success);
            Assert.NotEmpty(match.Groups[1].Value);
        }
    }
}
