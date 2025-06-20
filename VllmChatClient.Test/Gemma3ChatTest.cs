using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace VllmChatClient.Test
{
    public class Gemma3ChatTest
    {
        private readonly IChatClient _client;
        public Gemma3ChatTest()
        {
            _client = new VllmGemmaChatClient("http://localhost:8000/{0}/{1}", "", "gemma3");
        }
        private const string apiKey = "";
        [Description("��ȡָ���ص��������Ϣ")]
        static string GetWeather(string city) => Random.Shared.NextDouble() > 0.1 ? "It's sunny" : "It's raining";

        [Fact]
        public async Task ChatTest()
        {

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"����һ���������֣����ֽзƷ�"),
                new ChatMessage(ChatRole.User,"����˭��")
            };

            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);

            Assert.Equal(1, res.Messages.Count);
        }

        [Fact]
        public async Task ExtractTags()
        {
            string text = "�������Ǽ����ϲ�ѯ�����鵵ҵ�񣬰�����ѯ���ݡ����ء����⳵λ�Ȳ������Ǽǽ�����Լ����Ʒ��ݡ����ء����⳵λ�Ȳ������Ǽ�ԭʼ���ϡ�\n";


            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User,$"��Ϊ�����ı���ȡ3������صı�ǩ����json��ʽ���أ���Ҫ����˵����\n\n�ı�:{text}")
            };

            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);
            var match = Regex.Match(res.Messages.FirstOrDefault()?.Text, @"```json\s*(\[.*?\])\s*```", RegexOptions.Singleline);
            Assert.True(match.Success);
            string json = match.Groups[1].Value;
            Assert.NotEmpty(json);
            var list = JsonConvert.DeserializeObject<List<string>>(json);
        }

        [Fact]
        public async Task ChatFunctionCallTest()
        {

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"����һ���������֣����ֽзƷ�"),
                new ChatMessage(ChatRole.User,"������������Ҫ��ɡ��"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 3);//��������2�����ظ�1��

        }

        [Fact]
        public async Task ChatWithImageTest()
        {

            var userMessage = new ChatMessage(ChatRole.User, "����������ͼƬ�е�����");

            const string mediaType = "image/jpeg";
            await using (var fs = File.OpenRead("test.jpg"))
            {
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms);

                // DataContent(byte[] data, string mediaType)
                userMessage.Contents.Add(
                    new DataContent(ms.ToArray(), mediaType));
            }

            var messages = new List<ChatMessage> { userMessage };
            var chatoptions = new ChatOptions
            {
                Temperature = 0.5f,
                TopP = 0.9f,
            };
            var result = await _client.GetResponseAsync(messages, chatoptions);

            Assert.NotNull(result);
            Assert.True(result.Messages.Count == 1);
            Assert.True(result.Messages[0]?.Text.Contains("ͼƬ"));
        }

        [Fact]
        public async Task StreamChatWithImageTest()
        {

            var userMessage = new ChatMessage(ChatRole.User, "����ͼƬ�е�����");

            const string mediaType = "image/jpeg";
            var url = "https://chat.nngeo.net/api/attachfile/file?id=01JSMF861C1M6MGZHPMHPR2FQ8";
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    if (contentType != null && contentType.StartsWith("image/"))
                    {
                        // ����ͼƬ
                        var imageBytes = await response.Content.ReadAsByteArrayAsync();
                        var ms = new MemoryStream(imageBytes);
                        userMessage.Contents.Add(new DataContent(ms.ToArray(), contentType));

                    }

                }
            }

            var messages = new List<ChatMessage> { userMessage };
            var chatoptions = new ChatOptions
            {
                Temperature = 0.5f,
                TopP = 0.9f,
            };

            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                res += update;
            }
            Assert.True(res != null);

        }

        [Fact]
        public async Task StreamChatTest()
        {

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"����һ���������֣����ֽзƷ�"),
                new ChatMessage(ChatRole.User,"����˭��")
            };
            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                res += update;
            }
            Assert.True(res != null);
        }

        [Fact]
        public async Task StreamChatFunctionCallTest()
        {

            IChatClient client = new ChatClientBuilder(_client)
                .UseFunctionInvocation()
                .Build();
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"����һ���������֣����ֽзƷ�"),
                new ChatMessage(ChatRole.User,"������������Σ�")
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            string res = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
            {
                res += update;
            }
            Assert.True(res != null);
            Assert.Contains("��", res);    // ���� GetWeather ������У��
        }

        [Fact]
        public async Task ChatManualFunctionCallTest()
        {


            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"����һ���������֣����ֽзƷ�"),
                new ChatMessage(ChatRole.User,"������������Ҫ��ɡ��"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            var res = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 1);
            Assert.True(res.Messages[0].Contents.Count == 1);
            Assert.True(res.Messages[0].Contents[0] is FunctionCallContent);
            var functionCall = res.Messages[0].Contents[0] as FunctionCallContent;
            Assert.NotNull(functionCall);
            Assert.Equal("GetWeather", functionCall.Name);
            Assert.Equal("����", functionCall.Arguments["city"].ToString());

            messages.Add(res.Messages[0]);
            var functionResult = new FunctionResultContent(functionCall.CallId, "It's sunny");
            var contentList = new List<AIContent>();
            contentList.Add(functionResult);
            var functionResultMessage = new ChatMessage(ChatRole.Tool, contentList);
            messages.Add(functionResultMessage);
            var result = await _client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(result);
            Assert.Single(result.Messages);
            var answerText = result.Messages[0].Contents
                                   .OfType<TextContent>()
                                   .FirstOrDefault()?.Text;

            Assert.False(string.IsNullOrWhiteSpace(answerText));
        }

        [Fact]
        public async Task StreamChatManualFunctionCallTest()
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System,"����һ���������֣����ֽзƷ�"),
                new(ChatRole.User  ,"������������Σ�")
            };

            var chatOptions = new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(GetWeather)],
                ToolMode = ChatToolMode.Auto
            };

            var sb = new StringBuilder();
            ChatResponseUpdate? delta = null;

            await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
            {
                delta = update;
                AppendText(update, sb);      // ��׷�� TextContent

                if (update.FinishReason == ChatFinishReason.ToolCalls)
                    break;
            }

            int safeGuard = 0;              // ����ѭ��
            while (delta?.FinishReason == ChatFinishReason.ToolCalls && safeGuard++ < 8)
            {
                // ��ȡ���� FunctionCallContent�����ܲ�ֹһ����
                foreach (var fc in delta.Contents.OfType<FunctionCallContent>())
                {
                    // ����
                    Assert.Equal("GetWeather", fc.Name);
                    Assert.Equal("����", fc.Arguments["city"]?.ToString());

                    // д�� assistant(FunctionCall)
                    messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));

                    // ����ִ��
                    string result = ExecuteLocal(fc.Name, fc.Arguments);

                    // д�� tool(Result)
                    messages.Add(new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent(fc.CallId, result)]));
                }
                await foreach (var update in _client.GetStreamingResponseAsync(messages, chatOptions))
                {
                    delta = update;
                    AppendText(update, sb);

                    if (update.FinishReason == ChatFinishReason.ToolCalls ||
                        update.FinishReason == ChatFinishReason.Stop)
                        break;
                }
            }
            string res = sb.ToString();
            Assert.False(string.IsNullOrWhiteSpace(res));
            Assert.Contains("��", res);    // ���� GetWeather ������У��
        }


        private static void AppendText(ChatResponseUpdate update, StringBuilder sb)
        {
            foreach (var c in update.Contents.OfType<TextContent>())
                sb.Append(c.Text);
        }

       

        private static string ExecuteLocal(string name, IDictionary<string, object?> args) =>
            name switch
            {
                "GetWeather" => GetWeather(args["city"]!.ToString()!),
                "GetTime" => DateTime.Now.ToString("HH:mm"),
                _ => throw new NotSupportedException($"δ֪����: {name}")
            };

    }

}