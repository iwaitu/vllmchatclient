using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class DeepseekR1Test
    {
        private readonly IChatClient _client;
        private readonly ITestOutputHelper _output;
        public DeepseekR1Test(ITestOutputHelper testOutputHelper)
        {
            _output = testOutputHelper;
            var cloud_apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");
            _client = new VllmDeepseekR1ChatClient("https://dashscope.aliyuncs.com/compatible-mode/v1/{1}", cloud_apiKey, "deepseek-r1");
        }

        [Description("获取天气情况")]
        static string GetWeather([Description("城市名称")] string city) => $"{city} 气温35度，暴雨。";

        [Description("地名地址搜索")]
        static string Search([Description("需要搜索的目的地")] string question)
        {
            return "南宁市青秀区方圆广场北面站前路1号。";
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

            var reasonResponse = res as ReasoningChatResponse;

            _output.WriteLine("Reason: {0}", reasonResponse.Reason);
            _output.WriteLine("Reasone: {0}", reasonResponse.Text);
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
            string think = string.Empty;
            await foreach (ReasoningChatResponseUpdate update in _client.GetStreamingResponseAsync(messages))
            {
                var updateText = update.ToString();
                if (update is ReasoningChatResponseUpdate)
                {
                    if (update.Thinking)
                    {
                        think += updateText;
                    }
                    else
                    {
                        res += updateText;
                    }

                }
                
            }
            Assert.True(res != null);
            Assert.True(think != null);
            _output.WriteLine("Reasoning: " + think);
            _output.WriteLine("Response: " + res);
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
            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                if (update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var fc in update.Contents.OfType<FunctionCallContent>())
                    {
                        Assert.NotNull(fc);
                        messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));

                        string json = JsonSerializer.Serialize(
                            fc.Arguments,
                            new JsonSerializerOptions
                            {
                                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                        if (fc.Name == "GetWeather")
                        {
                            var result = GetWeather("南宁");
                            messages.Add(new ChatMessage(
                                ChatRole.Tool,
                                [new FunctionResultContent(fc.CallId, result)]));
                            continue;
                        }
                        else if (fc.Name == "Search")
                        {
                            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
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
            }
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.False(string.IsNullOrWhiteSpace(res));
            _output.WriteLine("Reasoning: " + reason);
            _output.WriteLine("Response: " + res);
        }
    }
}
