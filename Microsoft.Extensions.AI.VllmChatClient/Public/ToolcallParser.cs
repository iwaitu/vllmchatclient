
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.AI
{
    internal static class ToolcallParser
    {
        public static VllmFunctionToolCall ParseToolCall(string inputStr)
        {
            var pattern = @"<tool_call>(.*?)</tool_call>";
            var match = Regex.Match(inputStr, pattern, RegexOptions.Singleline);

            if (match.Success)
            {
                string content = match.Groups[1].Value;

                try
                {
                    // 尝试将内容解析为 JSON 对象
                    var jsonContent = JObject.Parse(content);

                    // 提取 "name" 和 "arguments"
                    JToken nameToken = jsonContent["name"];
                    JToken argumentsToken = jsonContent["arguments"];

                    if (nameToken != null && argumentsToken != null)
                    {
                        string name = nameToken.ToString();
                        var arguments = argumentsToken.ToString();
                        return new VllmFunctionToolCall { Name = name, Arguments = arguments };

                    }
                    else
                    {
                        return null;
                    }
                }
                catch (JsonReaderException)
                {
                    // JSON 解析失败
                    return null;
                }
            }

            // 如果未匹配到 <tool_call> 标签则返回 null
            return null;
        }

        public static IEnumerable<VllmFunctionToolCall> ParseToolCalls(string input,out string remainder)
        {
            var list = new List<VllmFunctionToolCall>();

            // 捕获所有 <tool_call>…</tool_call>
            var matches = Regex.Matches(input, @"<tool_call>([\s\S]*?)</tool_call>",
                                        RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var json = m.Groups[1].Value;
                var call = TryParseToolCallJson(json);   // 复用之前的 JSON→对象 方法
                if (call != null)
                    list.Add(call);
            }

            // 把所有 tool_call 块替换为空，保留其余文本
            remainder = Regex.Replace(input, @"<tool_call>[\s\S]*?</tool_call>",
                                      string.Empty, RegexOptions.Singleline);

            return list;
        }


        /// <summary>
        /// 尝试从 JSON 字符串中提取工具名称和 content 参数。
        /// </summary>
        /// <param name="json">JSON 格式字符串</param>
        /// <returns>成功时返回 (toolName, content)，失败时返回 (null, null)</returns>
        public static VllmFunctionToolCall TryParseToolCall(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);

                // 获取 name
                string name = doc.RootElement.GetProperty("name").GetString();

                // 获取 arguments.content
                var arguments = doc.RootElement.GetProperty("arguments");
                string content = arguments.GetProperty("content").GetString();

                return new VllmFunctionToolCall { Name = name, Arguments = content };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 连续文本中提取 0-N 个 JSON 对象；遇到转义字符/字符串时会正确忽略。
        /// 返回 (jsonFragments, rest) ：
        ///   jsonFragments  —— 已完整闭合的 JSON 片段列表
        ///   rest           —— 尚未闭合、需要继续累积的尾部文本
        /// </summary>
        public static (List<string> jsonFragments, string rest) SliceJsonFragments(string input)
        {
            var list = new List<string>();
            int depth = 0, start = -1;
            bool inString = false, escape = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start != -1)
                    {
                        list.Add(input.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            // ⚠️ 修正：如果没有 JSON，就把原串当 rest
            string rest = start == -1 && list.Count == 0 ? input
                          : start != -1 ? input.Substring(start)
                          : string.Empty;

            return (list, rest);
        }


        /// <summary>
        /// 尝试解析单个工具调用 JSON，返回 null 表示结构不符
        /// </summary>
        public static VllmFunctionToolCall? TryParseToolCallJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("name", out var nameProp) ||
                    !doc.RootElement.TryGetProperty("arguments", out var argsProp))
                    return null;

                return new VllmFunctionToolCall
                {
                    Name = nameProp.GetString(),
                    Arguments = argsProp.ToString()   // 保留原始 JSON 字符串
                };
            }
            catch
            {
                return null;        // 解析失败
            }
        }

    }
}
