using Microsoft.Extensions.AI;
using System.ComponentModel;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    /// <summary>
    /// Live compatibility tests for DeepSeek V4.1 Flash served by Aliyun DashScope.
    /// Requires the VLLM_ALIYUN_API_KEY environment variable; tests are skipped silently when it is absent.
    /// </summary>
    public class DeepseekV41FlashAliyunTests
    {
        private const string Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/{1}";
        private const string Model = "deepseek-v4.1-flash-1017";

        private readonly ITestOutputHelper _output;
        private readonly string? _apiKey = Environment.GetEnvironmentVariable("VLLM_ALIYUN_API_KEY");

        public DeepseekV41FlashAliyunTests(ITestOutputHelper output) => _output = output;

        private bool Skip => string.IsNullOrWhiteSpace(_apiKey);

        private IChatClient CreateClient() => new VllmDeepseekV4ChatClient(Endpoint, _apiKey, Model, new HttpClient());

        [Description("获取天气情况")]
        static string GetWeather([Description("城市名称")] string city) => $"{city} 气温35度，暴雨。";

        [Description("网络搜索工具，可以查询具体的地点、地址或即时信息")]
        static string Search([Description("需要搜索的问题或目的地")] string question) => "南宁市青秀区方圆广场北面站前路1号。";

        [Description("根据具体地址搜索周边的书店")]
        static string FindBookStore([Description("需要搜索的具体地址/门牌号")] string dest) => $"在 {dest} 附近100米有一家爱民书店。";

        [Fact]
        public async Task Chat_Thinking_Enabled()
        {
            if (Skip) return;
            var res = await CreateClient().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "你是谁？")],
                new VllmChatOptions { ThinkingEnabled = true });
            _output.WriteLine($"Type={res.GetType().Name} Reason={(res as ReasoningChatResponse)?.Reason}");
            _output.WriteLine($"Text={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task Chat_Thinking_Disabled()
        {
            if (Skip) return;
            var res = await CreateClient().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "你是谁？")],
                new VllmChatOptions { ThinkingEnabled = false });
            _output.WriteLine($"Text={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task Chat_NoOptions()
        {
            if (Skip) return;
            var res = await CreateClient().GetResponseAsync([new ChatMessage(ChatRole.User, "你是谁？")]);
            _output.WriteLine($"Text={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task Stream_Basic()
        {
            if (Skip) return;
            string text = "", think = "";
            await foreach (var u in CreateClient().GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "你是谁？")]))
            {
                if (u is ReasoningChatResponseUpdate r && r.Thinking) think += u.Text; else text += u.Text;
            }
            _output.WriteLine($"Think={think}\nText={text}");
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public async Task Chat_JsonResponseFormat()
        {
            if (Skip) return;
            var res = await CreateClient().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "以JSON格式返回 {\"city\":\"北京\"}")],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
            _output.WriteLine($"Text={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task Chat_FunctionCall_Serial()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateClient()).UseFunctionInvocation().Build();
            var res = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, "你是一个智能助手，遇到问题请优先调用工具解决。"),
                 new ChatMessage(ChatRole.User, "南宁火车站在哪里？我想到那附近去买书。")],
                new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)] });
            _output.WriteLine($"Messages={res.Messages.Count} Text={res.Text}");
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), res.Text);
        }

        [Fact]
        public async Task Stream_FunctionCall_Parallel()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateClient()).UseFunctionInvocation().Build();
            string text = "";
            await foreach (var u in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.System, "你是一个智能助手，遇到问题请优先调用工具解决。"),
                 new ChatMessage(ChatRole.User, "南宁火车站在哪里？南宁今天天气怎么样？")],
                new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)] }))
            {
                if (!(u is ReasoningChatResponseUpdate r && r.Thinking)) text += u.Text;
            }
            _output.WriteLine($"Text={text}");
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(text.Contains("方圆广场") || text.Contains("站前路") || text.Contains("35"), text);
        }

        [Fact]
        public async Task Chat_FunctionCall_ThinkingDisabled()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateClient()).UseFunctionInvocation().Build();
            var res = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "南宁天气怎么样？")],
                new VllmChatOptions { ThinkingEnabled = false, Tools = [AIFunctionFactory.Create(GetWeather)] });
            _output.WriteLine($"Text={res.Text}");
            Assert.Contains("35", res.Text);
        }

        private IChatClient CreateAnthropicClient() =>
            new VllmDeepseekV4ChatClient("https://dashscope.aliyuncs.com/apps/anthropic", _apiKey, Model, new HttpClient(), VllmApiMode.AnthropicMessages);

        private IChatClient CreateR1Client() => new VllmDeepseekR1ChatClient(Endpoint, _apiKey, Model, new HttpClient());

        [Fact]
        public async Task Anthropic_Chat()
        {
            if (Skip) return;
            var res = await CreateAnthropicClient().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "你是谁？")], new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 3000 });
            _output.WriteLine($"Reason={(res as ReasoningChatResponse)?.Reason}\nText={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task Anthropic_Stream()
        {
            if (Skip) return;
            string text = "", think = "";
            await foreach (var u in CreateAnthropicClient().GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "你是谁？")], new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 3000 }))
            {
                if (u is ReasoningChatResponseUpdate r && r.Thinking) think += u.Text; else text += u.Text;
            }
            _output.WriteLine($"Think={think}\nText={text}");
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        [Fact]
        public async Task Anthropic_FunctionCall()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateAnthropicClient()).UseFunctionInvocation().Build();
            var res = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "南宁火车站在哪里？我想到那附近去买书。")],
                new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 3000, Tools = [AIFunctionFactory.Create(Search), AIFunctionFactory.Create(FindBookStore)] });
            _output.WriteLine($"Text={res.Text}");
            Assert.True(res.Text.Contains("爱民书店") || res.Text.Contains("100米"), res.Text);
        }

        [Fact]
        public async Task Anthropic_Stream_FunctionCall()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateAnthropicClient()).UseFunctionInvocation().Build();
            string text = "";
            await foreach (var u in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "南宁火车站在哪里？南宁今天天气怎么样？")],
                new VllmChatOptions { ThinkingEnabled = true, MaxOutputTokens = 3000, Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)] }))
            {
                if (!(u is ReasoningChatResponseUpdate r && r.Thinking)) text += u.Text;
            }
            _output.WriteLine($"Text={text}");
            Assert.True(text.Contains("方圆广场") || text.Contains("站前路") || text.Contains("35"), text);
        }

        [Fact]
        public async Task R1Client_Chat()
        {
            if (Skip) return;
            var res = await CreateR1Client().GetResponseAsync([new ChatMessage(ChatRole.User, "你是谁？")], new VllmChatOptions { ThinkingEnabled = true });
            _output.WriteLine($"Reason={(res as ReasoningChatResponse)?.Reason}\nText={res.Text}");
            Assert.False(string.IsNullOrWhiteSpace(res.Text));
        }

        [Fact]
        public async Task R1Client_Stream_FunctionCall()
        {
            if (Skip) return;
            IChatClient client = new ChatClientBuilder(CreateR1Client()).UseFunctionInvocation().Build();
            string text = "";
            await foreach (var u in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "南宁火车站在哪里？南宁今天天气怎么样？")],
                new VllmChatOptions { ThinkingEnabled = true, Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(Search)] }))
            {
                if (!(u is ReasoningChatResponseUpdate r && r.Thinking)) text += u.Text;
            }
            _output.WriteLine($"Text={text}");
            Assert.True(text.Contains("方圆广场") || text.Contains("站前路") || text.Contains("35"), text);
        }

        [Fact]
        public async Task NoSafetyTagLeak_InReasoningOrAnswer()
        {
            if (Skip) return;
            var client = CreateClient();
            for (int i = 0; i < 10; i++)
            {
                var res = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new VllmChatOptions { ThinkingEnabled = true });
                Assert.DoesNotContain("ds_safety", res.Text);
                Assert.DoesNotContain("ds_safety", (res as ReasoningChatResponse)?.Reason ?? "");

                string all = "";
                await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], new VllmChatOptions { ThinkingEnabled = true }))
                {
                    all += u.Text;
                }
                Assert.DoesNotContain("ds_safety", all);
            }
        }

        [Fact]
        public async Task Chat_MultiTurn_WithPriorAssistant()
        {
            if (Skip) return;
            var res = await CreateClient().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "我叫小明。"),
                 new ChatMessage(ChatRole.Assistant, "你好，小明！"),
                 new ChatMessage(ChatRole.User, "我叫什么名字？")]);
            _output.WriteLine($"Text={res.Text}");
            Assert.Contains("小明", res.Text);
        }
    }
}
