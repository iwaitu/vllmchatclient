
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.AI
{
    internal static class ToolcallParser
    {
        public static VllmFunctionToolCall? ParseToolCall(string inputStr)
        {
            var pattern = @"<tool_call>(.*?)</tool_call>";
            var match = Regex.Match(inputStr, pattern, RegexOptions.Singleline);

            if (match.Success)
            {
                string content = match.Groups[1].Value;

                try
                {
                    using var jsonContent = JsonDocument.Parse(content);
                    if (jsonContent.RootElement.TryGetProperty("name", out var nameToken) &&
                        jsonContent.RootElement.TryGetProperty("arguments", out var argumentsToken))
                    {
                        string name = nameToken.GetString() ?? string.Empty;
                        var arguments = GetArgumentText(argumentsToken);
                        return new VllmFunctionToolCall { Name = name, Arguments = arguments };
                    }

                    return null;
                }
                catch (JsonException)
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

            var gemma4Matches = Regex.Matches(
                input,
                @"<\|tool_call>call:(?<name>[A-Za-z_][A-Za-z0-9_]*)\{(?<args>[\s\S]*?)\}<tool_call\|>",
                RegexOptions.Singleline);

            foreach (Match match in gemma4Matches)
            {
                var call = TryParseGemma4ToolCall(match.Groups["name"].Value, match.Groups["args"].Value);
                if (call != null)
                {
                    list.Add(call);
                }
            }

            // 把所有 tool_call 块替换为空，保留其余文本
            remainder = Regex.Replace(input, @"<tool_call>[\s\S]*?</tool_call>",
                                      string.Empty, RegexOptions.Singleline);
            remainder = Regex.Replace(remainder, @"<\|tool_call>call:[A-Za-z_][A-Za-z0-9_]*\{[\s\S]*?\}<tool_call\|>",
                                      string.Empty, RegexOptions.Singleline);

            return list;
        }


        /// <summary>
        /// 尝试从 JSON 字符串中提取工具名称和 content 参数。
        /// </summary>
        /// <param name="json">JSON 格式字符串</param>
        /// <returns>成功时返回 (toolName, content)，失败时返回 (null, null)</returns>
        public static VllmFunctionToolCall? TryParseToolCall(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);

                // 获取 name
                string? name = doc.RootElement.GetProperty("name").GetString();

                // 获取 arguments.content
                var arguments = doc.RootElement.GetProperty("arguments");
                string? content = arguments.GetProperty("content").GetString();

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
                        var candidate = input.Substring(start, i - start + 1);

                        // ▶ 尝试真正解析
                        if (IsValidJson(candidate))
                            list.Add(candidate);           // ✅ 合法，收入结果
                        else
                            depth = 0;                     // ❌ 非法，丢弃，本轮不算
                        start = -1;
                    }
                }
            }

            string rest =
                // 1) 有未闭合 JSON
                start != -1 ? input.Substring(start) :
                // 2) 无 JSON 片段被收集
                list.Count == 0 ? input :
                                              // 3) 全部 JSON 已切出，无残留
                                              string.Empty;

            return (list, rest);
        }

        private static bool IsValidJson(string json)
        {
            try
            {
                using var _ = JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// 尝试解析单个工具调用 JSON，返回 null 表示结构不符
        /// </summary>
        public static VllmFunctionToolCall? TryParseToolCallJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!TryParseToolCallElement(doc.RootElement, out var call))
                {
                    return null;
                }

                return call;
            }
            catch
            {
                return null;        // 解析失败
            }
        }

        public static bool TryParseBareToolCallsJson(string input, out List<VllmFunctionToolCall> calls)
        {
            calls = new List<VllmFunctionToolCall>();

            if (string.IsNullOrWhiteSpace(input) || !StartsWithJsonContainer(input))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(input.Trim());
                if (!TryParseToolCallElements(doc.RootElement, calls))
                {
                    return false;
                }

                return calls.Count > 0;
            }
            catch
            {
                calls.Clear();
                return false;
            }
        }

        public static bool TryExtractBareToolCallsJson(ref string input, out List<VllmFunctionToolCall> calls)
        {
            calls = new List<VllmFunctionToolCall>();

            if (string.IsNullOrWhiteSpace(input) || !ContainsJsonContainerStart(input))
            {
                return false;
            }

            var ranges = new List<(int Start, int Length)>();
            foreach (var fragment in EnumerateCompleteJsonFragments(input))
            {
                if (TryParseBareToolCallsJson(fragment.Json, out var parsedCalls))
                {
                    calls.AddRange(parsedCalls);
                    ranges.Add((fragment.Start, fragment.Json.Length));
                }
            }

            if (calls.Count == 0)
            {
                return false;
            }

            for (var i = ranges.Count - 1; i >= 0; i--)
            {
                var range = ranges[i];
                input = input.Remove(range.Start, range.Length);
            }

            input = StripJsonCodeFenceResidues(input);
            return true;
        }

        public static bool StartsWithJsonContainer(string input)
        {
            var trimmed = input.AsSpan().TrimStart();
            return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
        }

        public static bool ContainsJsonContainerStart(string input)
        {
            foreach (var c in input)
            {
                if (c == '{' || c == '[')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从累积缓冲区中提取所有<strong>已完整闭合</strong>的 &lt;tool_call&gt;…&lt;/tool_call&gt;，
        /// 并在成功时把这些块从 <paramref name="buffer"/> 中删除。
        /// </summary>
        /// <param name="buffer">外层传入的可变字符串（通常是 StringBuilder.ToString() 的缓存）</param>
        /// <param name="calls">返回解析成功的工具调用列表</param>
        /// <returns>是否至少解析到一个工具调用</returns>
        /// <summary>
        /// 解析缓冲区中所有<strong>已闭合</strong>的 &lt;tool_call&gt;…&lt;/tool_call&gt;，
        /// 把成功解析的 JSON 转成 <see cref="VllmFunctionToolCall"/> 放入 <paramref name="calls"/>，
        /// 无论解析是否成功，都会把对应块以及其后紧跟的“悬挂右大括号 + 空白”从 <paramref name="buffer"/> 中删除。
        /// </summary>
        public static bool TryFlushClosedToolCallBlocks(
         ref string buffer,
         out List<VllmFunctionToolCall> calls)
        {
            calls = new List<VllmFunctionToolCall>();

            // Regex – 非贪婪匹配，确保最先遇到的 </tool_call> 就闭合
            const string pattern = @"<tool_call>([\s\S]*?)</tool_call>";
            var matches = Regex.Matches(buffer, pattern, RegexOptions.Singleline);

            if (matches.Count == 0)               // 没有任何闭合块
                return false;

            foreach (Match m in matches)
            {
                string innerJson = m.Groups[1].Value;
                var call = TryParseToolCallJson(innerJson);  // 复用 JSON→对象 方法
                if (call != null)
                    calls.Add(call);
            }

            var gemma4Matches = Regex.Matches(
                buffer,
                @"<\|tool_call>call:(?<name>[A-Za-z_][A-Za-z0-9_]*)\{(?<args>[\s\S]*?)\}<tool_call\|>",
                RegexOptions.Singleline);

            foreach (Match match in gemma4Matches)
            {
                var call = TryParseGemma4ToolCall(match.Groups["name"].Value, match.Groups["args"].Value);
                if (call != null)
                {
                    calls.Add(call);
                }
            }

            // 把已解析的块整体从 buffer 中移除
            buffer = Regex.Replace(buffer, pattern, string.Empty,
                                   RegexOptions.Singleline);
            buffer = Regex.Replace(buffer, @"<\|tool_call>call:[A-Za-z_][A-Za-z0-9_]*\{[\s\S]*?\}<tool_call\|>", string.Empty,
                                   RegexOptions.Singleline);
            return calls.Count > 0;
        }


        public static bool IsInsideIncompleteToolCall(string text)
        {
            int open = text.LastIndexOf("<tool_call>", StringComparison.Ordinal);
            int close = text.LastIndexOf("</tool_call>", StringComparison.Ordinal);
            // 出现 <tool_call> 但尚未出现对应的 </tool_call>
            if (open != -1 && close < open)
            {
                return true;
            }

            int gemmaOpen = text.LastIndexOf("<|tool_call>", StringComparison.Ordinal);
            int gemmaClose = text.LastIndexOf("<tool_call|>", StringComparison.Ordinal);
            return gemmaOpen != -1 && gemmaClose < gemmaOpen;
        }

        public static int GetBraceDepth(string text)
        {
            int depth = 0; bool inStr = false; bool esc = false;
            foreach (char c in text)
            {
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                if (c == '}') depth--;
            }
            return depth; // >0 说明还有 “{” 没闭合
        }

        /// <summary>
        /// 清理仍残留在文本中的 &lt;tool_call&gt; 标签及其衍生噪声，
        /// 同时保留 <think>…</think> 思维链内容不受影响。<br/>
        /// ⚠️ 本函数假设：真正的工具调用块 **已经** 被解析成 FunctionCallContent，<br/>
        /// 这里只负责把遗留的标签、孤立大括号等“脏字符”从待展示文本里剔除。
        /// </summary>
        public static string StripToolCallResidues(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = Regex.Replace(text, @"</?tool_call>", string.Empty,
                                 RegexOptions.IgnoreCase);
            text = text.Replace("<|tool_call>", string.Empty, StringComparison.OrdinalIgnoreCase)
                       .Replace("<tool_call|>", string.Empty, StringComparison.OrdinalIgnoreCase)
                       .Replace("<|tool_response>", string.Empty, StringComparison.OrdinalIgnoreCase)
                       .Replace("<tool_response|>", string.Empty, StringComparison.OrdinalIgnoreCase);

            text = Regex.Replace(text,
                @"[ \t]*}[ \t]*(?=\r?\n|$)", string.Empty);          // 行尾孤立 }
            text = Regex.Replace(text,
                @"(?<=\A|\r?\n)[ \t]*{\s*", string.Empty);           // 行首孤立 {

            return text;   // 整体都不 Trim，保留任何前导空格
        }

        public static bool HasUnclosedToolCall(string input)
        {
            // 查找所有起始和结束标签位置
            int openCount = Regex.Matches(input, @"<tool_call>").Count;
            int closeCount = Regex.Matches(input, @"</tool_call>").Count;

            openCount += Regex.Matches(input, @"<\|tool_call>").Count;
            closeCount += Regex.Matches(input, @"<tool_call\|>").Count;

            return openCount != closeCount;
        }

        private static VllmFunctionToolCall? TryParseGemma4ToolCall(string name, string args)
        {
            try
            {
                var arguments = new Dictionary<string, object?>();

                foreach (Match match in Regex.Matches(args, @"(\w+):(?:<\|""\|>(.*?)<\|""\|>|([^,}]*))", RegexOptions.Singleline))
                {
                    string key = match.Groups[1].Value;
                    string rawValue = (match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value).Trim();
                    arguments[key] = CastGemma4Value(rawValue);
                }

                return new VllmFunctionToolCall
                {
                    Name = name,
                    Arguments = JsonSerializer.Serialize(
                        arguments,
                        typeof(Dictionary<string, object?>),
                        JsonContext.Default)
                };
            }
            catch
            {
                return null;
            }
        }

        private static object? CastGemma4Value(string value)
        {
            if (int.TryParse(value, out var intValue))
            {
                return intValue;
            }

            if (double.TryParse(value, out var doubleValue))
            {
                return doubleValue;
            }

            if (bool.TryParse(value, out var boolValue))
            {
                return boolValue;
            }

            return value.Trim('\'', '"');
        }

        private static string GetArgumentText(JsonElement arguments)
            => arguments.ValueKind == JsonValueKind.String
                ? arguments.GetString() ?? string.Empty
                : arguments.GetRawText();

        private static IEnumerable<(int Start, string Json)> EnumerateCompleteJsonFragments(string input)
        {
            var stack = new Stack<char>();
            var start = -1;
            var inString = false;
            var escape = false;

            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];

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

                if (c == '{' || c == '[')
                {
                    if (stack.Count == 0)
                    {
                        start = i;
                    }

                    stack.Push(c == '{' ? '}' : ']');
                    continue;
                }

                if ((c == '}' || c == ']') && stack.Count > 0)
                {
                    var expected = stack.Pop();
                    if (c != expected)
                    {
                        stack.Clear();
                        start = -1;
                        continue;
                    }

                    if (stack.Count == 0 && start >= 0)
                    {
                        var json = input.Substring(start, i - start + 1);
                        if (IsValidJson(json))
                        {
                            yield return (start, json);
                        }

                        start = -1;
                    }
                }
            }
        }

        private static string StripJsonCodeFenceResidues(string input)
        {
            input = Regex.Replace(input, @"```(?:json)?", string.Empty, RegexOptions.IgnoreCase);
            return input.Replace("```", string.Empty, StringComparison.Ordinal).Trim();
        }

        private static bool TryParseToolCallElement(JsonElement element, out VllmFunctionToolCall call)
        {
            call = new VllmFunctionToolCall();

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var source = element;
            if (element.TryGetProperty("function", out var functionProp) &&
                functionProp.ValueKind == JsonValueKind.Object)
            {
                source = functionProp;
            }

            if (!source.TryGetProperty("name", out var nameProp) ||
                nameProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameProp.GetString()) ||
                (!source.TryGetProperty("arguments", out var argsProp) &&
                 !source.TryGetProperty("parameters", out argsProp) &&
                 !source.TryGetProperty("input", out argsProp)))
            {
                return false;
            }

            call = new VllmFunctionToolCall
            {
                Name = nameProp.GetString(),
                Arguments = GetArgumentText(argsProp)
            };
            return true;
        }

        private static bool TryParseToolCallElements(JsonElement element, List<VllmFunctionToolCall> calls)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (!TryParseToolCallElement(item, out var call))
                    {
                        calls.Clear();
                        return false;
                    }

                    calls.Add(call);
                }

                return calls.Count > 0;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryParseToolCallElement(element, out var directCall))
            {
                calls.Add(directCall);
                return true;
            }

            foreach (var propertyName in new[] { "tool_call", "tool_calls", "function_call", "function_calls" })
            {
                if (!element.TryGetProperty(propertyName, out var nestedCalls))
                {
                    continue;
                }

                return TryParseToolCallElements(nestedCalls, calls);
            }

            return false;
        }

    }
}
