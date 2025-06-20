using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            _client = new VllmGemmaChatClient("http://localhost:8000/{0}/{1}", "sk-96448b7e99da436d97fe173643518055", "gemma3");
        }
        private const string apiKey = "";
        [Description("获取指定地点的天气信息")]
        static string GetWeather(string city) => Random.Shared.NextDouble() > 0.1 ? "It's sunny" : "It's raining";

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
            Assert.True(res.Messages.FirstOrDefault()?.Text.Contains("菲菲"));
        }

        [Fact]
        public async Task ExtractTags()
        {
            string text = """
                                南宁市不动产登记中心新建房屋办证业务指南
                文档说明：
                该文档内容仅适用于新建房屋买卖，严禁用于二手房过户；
                该文档提到的所有内容仅适用于国有建设用地及国有建设用地上的房屋，该文档提到的商品房、车位车库、经济适用房、限价商品房、市场运作房、全额集资房、房改房、危旧房改住房改造均是指国有建设用地上的房屋。

                二、基础概念
                新建房屋
                新建房屋的概念
                指开发商或有关单位在建设工程项目完工后，第一次投入市场销售的全新房屋，也称为一手房、增量房。 只有向开发商或者售房单位买的房，才是新建房屋。 新建房屋的房屋类型：
                商品房（含商铺、公寓、办公用房、高层次人才房、市场运作房、车位、车库、限价商品房）、市场运作房、全额集资房、房改房、危旧房改住房改造、经济适用房。 新建房屋办证
                新建房屋办证的概念：
                购买新建房屋后，将房屋产权由开发商或售房单位转移登记到购房者名下的过程，也称为一手房办证、一手房过户、增量房过户、增量房办证。 新建房屋办证的业务类型
                新建房屋买卖。

                三、通用规则与业务逻辑关联规则
                材料规则说明：
                √通用=通用必交材料，所有业务必须提供。

                √房屋=房屋类型补充材料，按房屋类型补充。
                """;

            string systemPrompt = """
                                你是“中文百科知识库”的标签提取助手，请严格遵循以下规则输出最相关标签：

                🔹 *标签要求*

                1. 标签需与用户输入语言一致。
                2. 若用户提供中文内容，则提取中文短语（2–8字），应为名词或名词短语，直接代表文本的业务主题或材料类别。
                3. 若用户提供英文内容，则提取英文单词或短语（以名词为主），能直接代表文本的关键内容，如人物、地点、年份等。
                4. 标签要求具体，避免抽象、泛化词语（如“材料”“流程”“事项”“政策”“法律”），优先选择更具体且有实际意义的词汇。
                5. 禁止包含无关信息，如与正文无关的人名、日期、标点、空格、数字编号，以及双引号或其他非法字符。
                6. 标签之间互不重复，且不应是上下位关系；优先选择更具体者。
                7. 标签格式限制：
                    7.1 中文标签仅允许中文汉字、数字；
                    7.2 英文标签仅允许英文字母、数字。
                    7.3 所有标签不允许出现双引号或其他非法字符。
                8. 仅输出符合以上要求的标签，格式为合法的 JSON 数组（如：["不动产登记","抵押证明"] 或 ["property registration","mortgage certificate"]）。

                🔹 *输出格式*
                仅返回一行合法 JSON，字段顺序不可改变：
                {"tags":["标签1","标签2","标签3"]}

                🔹 *示例*（few‑shot）
                【示例1】
                文本：
                办理抵押贷款登记时，申请人需提交不动产权证书、主债权合同、抵押合同等材料。
                返回：
                {"tags":["抵押贷款登记","不动产权证书","抵押合同"]}

                【示例2】
                文本：
                房屋继承登记适用于被继承人死亡后，其合法继承人申请将房屋产权过户到继承人名下。
                返回：
                {"tags":["房屋继承登记","继承人","产权过户"]}
                """;

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System,systemPrompt),
                new ChatMessage(ChatRole.User,$"请为以下文本提取3个最相关的标签。用json格式返回，不要其他说明。\n\n文本:{text}")
            };

            var res = await _client.GetResponseAsync(messages);
            Assert.NotNull(res);
            var raw = res.Messages.FirstOrDefault()?.Text;
            var match = Regex.Match(raw, @"\{""tags""\s*:\s*\[[\s\S]*?\]\}", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                var match1 = Regex.Match(raw, @"\{\s*\{[\s\S]*?\}\s*\}");
                if (match1.Success)
                {
                    match = Regex.Match(match1.Value, @"\{""tags""\s*:\s*\[[\s\S]*?\]\}", RegexOptions.IgnoreCase);
                }
            }

            Assert.True(match.Success);

            string json = match.Groups[0].Value;
            Assert.NotEmpty(json);
            
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
                new ChatMessage(ChatRole.User,"在南宁，我需要带伞吗？"),
            };
            ChatOptions chatOptions = new()
            {
                Tools = [AIFunctionFactory.Create(GetWeather)]
            };
            var res = await client.GetResponseAsync(messages, chatOptions);
            Assert.NotNull(res);
            Assert.True(res.Messages.Count == 3);//函数调用2条，回复1条

        }

        [Fact]
        public async Task ChatWithImageTest()
        {

            var userMessage = new ChatMessage(ChatRole.User, "用中文描述图片中的内容");

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
            Assert.True(result.Messages[0]?.Text.Contains("图片"));
        }

        [Fact]
        public async Task StreamChatWithImageTest()
        {

            var userMessage = new ChatMessage(ChatRole.User, "描述图片中的内容");

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
                        // 处理图片
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
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"你是谁？")
            };
            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                res += update;
            }
            Assert.True(res != null);
            Assert.False(res.StartsWith("<"));
            Assert.True(res.Contains("菲菲"));
        }

        [Fact]
        public async Task StreamChatJsonTest()
        {

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲，回答问题不能使用codeblock"),
                new ChatMessage(ChatRole.User,"用json 格式输出你的名字，不要输出氤内容。例如:{ 'name': '小李'}")
            };
            string res = string.Empty;
            await foreach (var update in _client.GetStreamingResponseAsync(messages))
            {
                res += update;
            }
            Assert.True(res != null);
            Assert.False(res.StartsWith("<"));
            Assert.True(res.Contains("菲菲"));
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
                new ChatMessage(ChatRole.User,"南宁的天气如何？")
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
            Assert.False(res.StartsWith("<"));
            Assert.Contains("晴", res);    // 根据 GetWeather 假设结果校验
        }

        [Fact]
        public async Task ChatManualFunctionCallTest()
        {


            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
                new ChatMessage(ChatRole.User,"在南宁，我需要带伞吗？"),
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
            Assert.Equal("南宁", functionCall.Arguments["city"].ToString());

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
                new(ChatRole.System,"你是一个智能助手，名字叫菲菲"),
                new(ChatRole.User  ,"南宁的天气如何？")
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
                AppendText(update, sb);      // 仅追加 TextContent

                if (update.FinishReason == ChatFinishReason.ToolCalls)
                    break;
            }

            int safeGuard = 0;              // 防死循环
            while (delta?.FinishReason == ChatFinishReason.ToolCalls && safeGuard++ < 8)
            {
                // 提取所有 FunctionCallContent（可能不止一个）
                foreach (var fc in delta.Contents.OfType<FunctionCallContent>())
                {
                    // 断言
                    Assert.Equal("GetWeather", fc.Name);
                    Assert.Equal("南宁", fc.Arguments["city"]?.ToString());

                    // 写回 assistant(FunctionCall)
                    messages.Add(new ChatMessage(ChatRole.Assistant, [fc]));

                    // 本地执行
                    string result = ExecuteLocal(fc.Name, fc.Arguments);

                    // 写回 tool(Result)
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
            Assert.False(res.StartsWith("<"));
            Assert.Contains("晴", res);    // 根据 GetWeather 假设结果校验
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
                _ => throw new NotSupportedException($"未知函数: {name}")
            };

    }

}