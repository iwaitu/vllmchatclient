using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;


using System.Runtime.CompilerServices;
namespace Microsoft.Extensions.AI
{
    public sealed class VllmGlmZ1ChatClient : IChatClient
    {
        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;

        /// <summary>关于客户端的元数据</summary>
        private readonly ChatClientMetadata _metadata;

        /// <summary>api/chat 端点 URI</summary>
        private readonly string _apiChatEndpoint;

        /// <summary>用于发送请求的 HttpClient 对象</summary>
        private readonly HttpClient _httpClient;


        /// <summary>用于与工具调用参数和结果相关的序列化活动的 JsonSerializerOptions 对象</summary>
        private JsonSerializerOptions _toolCallJsonSerializerOptions = AIJsonUtilities.DefaultOptions;

        /// <summary>初始化 VllmChatClient 类的新实例</summary>
        /// <param name="endpoint">vllm 服务托管的端点 URI。格式 "http://localhost:8000/{0}/{1}"</param>
        /// <param name="modelId">
        /// 要使用的模型的 ID。该 ID 也可以通过 <see cref="ChatOptions.ModelId"/> 在每个请求中覆盖。
        /// 必须提供有效的模型 ID，可以通过此参数或 <see cref="ChatOptions.ModelId"/> 提供。
        /// </param>
        /// <param name="httpClient">用于 HTTP 操作的 HttpClient 实例</param>
        public VllmGlmZ1ChatClient(string endpoint, string? token = null, string? modelId = "glm4", HttpClient? httpClient = null)
        {
            _ = Throw.IfNull(endpoint);
            if (modelId is not null)
            {
                _ = Throw.IfNullOrWhitespace(modelId);
            }

            _apiChatEndpoint = endpoint ?? "http://localhost:8000/{0}/{1}";

            _httpClient = httpClient ?? VllmUtilities.SharedClient;


            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            //_apiToken = token ?? "0";

            _metadata = new("vllm", new Uri(_apiChatEndpoint), modelId);
        }

        public JsonSerializerOptions ToolCallJsonSerializerOptions
        {
            get => _toolCallJsonSerializerOptions;
            set => _toolCallJsonSerializerOptions = Throw.IfNull(value);
        }

        public void Dispose()
        {
            if (_httpClient != VllmUtilities.SharedClient)
            {
                _httpClient.Dispose();
            }
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // 检查 messages 参数是否为 null
            _ = Throw.IfNull(messages);
            // 获取 funcNames
            var funcNames = options?.Tools?.OfType<AIFunction>().Select(f => f.Name).ToArray();
            string apiEndpoint = string.Format(_apiChatEndpoint, "v1", "chat/completions");
            using var httpResponse = await _httpClient.PostAsJsonAsync(
                apiEndpoint,
                ToVllmChatRequest(messages, options, stream: false),
                JsonContext.Default.VllmChatRequest,
                cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            var test = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var response = (await httpResponse.Content.ReadFromJsonAsync(
                JsonContext.Default.VllmChatResponse,
                cancellationToken).ConfigureAwait(false))!;

            if (response.Choices is null || response.Choices.Length == 0)
            {
                throw new InvalidOperationException("未返回任何响应选项。");
            }

            return new(FromVllmMessage(response.Choices[0].Message ?? new VllmChatResponseMessage { Role = "assistant" }, funcNames ?? []))
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = ToFinishReason(response.Choices[0].FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseVllmChatResponseUsage(response),
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return
                serviceKey is not null ? null :
                serviceType == typeof(ChatClientMetadata) ? _metadata :
                serviceType.IsInstanceOfType(this) ? this :
                null;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 检查 messages 参数是否为 null
            _ = Throw.IfNull(messages);

            // 获取 funcNames
            var funcNames = options?.Tools?.OfType<AIFunction>().Select(f => f.Name).ToList();
            string apiEndpoint = string.Format(_apiChatEndpoint, "v1", "chat/completions");
            using HttpRequestMessage request = new(HttpMethod.Post, apiEndpoint)
            {
                Content = JsonContent.Create(ToVllmChatRequest(messages, options, stream: true), JsonContext.Default.VllmChatRequest)
            };
            using var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            // vllm 在流式传输时不会在每个数据块中设置响应 ID，因此我们需要生成一个
            var responseId = Guid.NewGuid().ToString("N");

            using var httpResponseStream = await httpResponse.Content
#if NET
                .ReadAsStreamAsync(cancellationToken)
#else
                .ReadAsStreamAsync()
#endif
                .ConfigureAwait(false);

            using var streamReader = new StreamReader(httpResponseStream);
            string buffer_msg = string.Empty;
            string functionName = string.Empty;
            int maxFunctionNameLength = funcNames?.Any() == true ? funcNames.Max(s => s.Length) : 0; // Added null check for funcNames
            bool thinking = true;
            bool closeBuff = false;
            yield return new ReasoningChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = null,
                ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = responseId,
                Thinking = true,
                Role = new ChatRole("assistant"),
                Contents = new List<AIContent> { new TextContent("\n<think>\n") },
            };
#if NET
            while ((await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { } line)
#else
            while ((await streamReader.ReadLineAsync().ConfigureAwait(false)) is { } line)
#endif
            {
                // 去除 "data:" 前缀
                string jsonPart = Regex.Replace(line, @"^data:\s*", "");

                if (string.IsNullOrEmpty(jsonPart))
                {
                    continue;
                }
                else if (jsonPart == "[DONE]")
                {
                    break;
                }



                var chunk = JsonSerializer.Deserialize(jsonPart, JsonContext.Default.VllmChatStreamResponse);

                if (chunk == null || chunk.Choices is null || chunk.Choices.Count == 0)
                {
                    continue;
                }
                string? modelId = chunk.Model ?? _metadata.DefaultModelId;

                ReasoningChatResponseUpdate update = new()
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                    FinishReason = ToFinishReason(chunk.Choices.FirstOrDefault()?.FinishReason),
                    ModelId = modelId,
                    Thinking = thinking,
                    Role = chunk.Choices.FirstOrDefault()?.Delta?.Role is { } role ? new ChatRole(role) : null,
                };

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
                {
                    if (message.Content?.Length > 0 || update.Contents.Count == 0)
                    {
                        buffer_msg += message.Content;
                        if (buffer_msg.Contains("</think>") && thinking == true)
                        {
                            thinking = false;
                            update.Contents.Insert(0, new TextContent(message.Content));
                            yield return update;
                            buffer_msg = string.Empty;
                            continue;
                        }
                        if( thinking == true)
                        {
                            update.Contents.Insert(0, new TextContent(message.Content));
                            yield return update;
                            continue;

                        }
                        if (funcNames?.Any(p => p == buffer_msg) == true) // Added null check for funcNames
                        {
                            functionName = buffer_msg;
                            buffer_msg = string.Empty;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(functionName))
                            {
                                if (buffer_msg.StartsWith("\n"))
                                {
                                    var func = ProcessStreamChunkGlm(functionName + buffer_msg, funcNames ?? []);
                                    if (func != null)
                                    {
                                        update.Contents.Add(ToFunctionCallContent(func));
                                        update.FinishReason = ChatFinishReason.ToolCalls;
                                        yield return update;
                                        yield break;
                                    }
                                }
                            }
                            else
                            {
                                if (closeBuff == false)
                                {
                                    if (buffer_msg.Length > maxFunctionNameLength)
                                    {
                                        closeBuff = true;
                                        update.Contents.Insert(0, new TextContent(buffer_msg));
                                        buffer_msg = string.Empty;

                                    }
                                }
                                else
                                {
                                    update.Contents.Insert(0, new TextContent(message.Content));
                                }


                            }

                        }
                    }
                }
                yield return update;
            }

        }

        /// <summary>
        /// 解析 VllmChatResponse 中的使用信息
        /// </summary>
        private static UsageDetails? ParseVllmChatResponseUsage(VllmChatResponse response)
        {
            if (response?.Usage == null)
                return null;

            return new UsageDetails
            {
                InputTokenCount = response.Usage.PromptTokens,
                OutputTokenCount = response.Usage.CompletionTokens,
                TotalTokenCount = response.Usage.TotalTokens
            };
        }

        /// <summary>
        /// 将响应中的结束原因转换为内部枚举
        /// </summary>
        private static ChatFinishReason? ToFinishReason(string? reason) =>
            reason switch
            {
                null => null,
                "length" => ChatFinishReason.Length,
                "stop" => ChatFinishReason.Stop,
                _ => new ChatFinishReason(reason),
            };

        /// <summary>
        /// 将 Vllm 返回的消息转换为 ChatMessage 对象
        /// </summary>
        private static ChatMessage FromVllmMessage(VllmChatResponseMessage message, IEnumerable<string> funcNames)
        {
            List<AIContent> contents = [];

            if (message.Content?.Length > 0 || contents.Count == 0)
            {
                var functionCall = ProcessStreamChunkGlm(message.Content, funcNames);
                if (functionCall != null)
                {
                    contents.Add(ToFunctionCallContent(functionCall));
                }
                else
                {
                    contents.Insert(0, new TextContent(message.Content));
                }
            }

            return new ChatMessage(new(message.Role ?? "assistant"), contents);
        }

        /// <summary>
        /// 处理流式数据块中的工具调用标签
        /// </summary>
        private static VllmFunctionToolCall? ProcessStreamChunkGlm(string? buffer, IEnumerable<string> funcNames)
        {
            if (string.IsNullOrWhiteSpace(buffer) || funcNames == null || !funcNames.Any()) // Added null and empty checks for funcNames
            {
                return null;
            }
            // 创建一个正则表达式，匹配函数名和 JSON 参数
            string namesPattern = string.Join("|", funcNames.Select(f => Regex.Escape(f)));
            string pattern = $@"^\s*(?<fn>{namesPattern})\s*(?:\r?\n(?<json>\{{.*\}})|(?<json_inline>\{{.*\}}))\s*$";

            var match = Regex.Match(buffer.Trim(), pattern, RegexOptions.Singleline);
            if (!match.Success)
            {
                return null;
            }

            string fn = match.Groups["fn"].Value;
            string jsonStr = match.Groups["json"].Success ? match.Groups["json"].Value : match.Groups["json_inline"].Value;

            try
            {
                // 尝试解析 JSON 参数
                var parameters = JsonConvert.DeserializeObject(jsonStr);
                return new VllmFunctionToolCall { Name = fn, Arguments = jsonStr };  // 返回 VllmFunctionToolCall
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }



        /// <summary>
        /// 将工具调用转换为 FunctionCallContent 对象
        /// </summary>
        private static FunctionCallContent ToFunctionCallContent(VllmFunctionToolCall function)
        {
#if NET
            var id = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
            var id = Guid.NewGuid().ToString().Substring(0, 8);
#endif
            var arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(function.Arguments ?? "{}");
            return new FunctionCallContent(id, function.Name ?? "", arguments);
        }

        /// <summary>
        /// 根据响应格式参数获取 VllmChatResponseFormat 对象
        /// </summary>
        private static JsonElement? ToVllmChatResponseFormat(ChatResponseFormat? format)
        {
            if (format is ChatResponseFormatJson jsonFormat)
            {
                return jsonFormat.Schema ?? _schemalessJsonResponseFormatValue;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 根据传入的消息和选项构造 VllmChatRequest 请求对象
        /// </summary>
        private VllmChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            VllmChatRequest request = new()
            {
                Format = ToVllmChatResponseFormat(options?.ResponseFormat),
                Messages = messages.SelectMany(ToVllmChatRequestMessages).ToArray(),
                Model = options?.ModelId ?? _metadata.DefaultModelId ?? string.Empty,
                Stream = stream,
                Tools = options?.ToolMode is not NoneChatToolMode && options?.Tools is { Count: > 0 } tools ? tools.OfType<AIFunction>().Select(ToVllmTool) : null,
            };

            if (options is not null)
            {
                if (options.Temperature is float temperature)
                {
                    (request.Options ??= new()).temperature = temperature;
                }

                if (options.TopP is float topP)
                {
                    (request.Options ??= new()).top_p = topP;
                }
            }

            return request;
        }

        /// <summary>
        /// 将 ChatMessage 对象转换为一个或多个 VllmChatRequestMessage 消息
        /// </summary>
        private IEnumerable<VllmChatRequestMessage> ToVllmChatRequestMessages(ChatMessage msg)
        {
            VllmChatRequestMessage? pendingText = null;   // 用来缓存 TextContent
            var contents = msg.Contents;

            foreach (var part in msg.Contents)
            {
                switch (part)
                {
                    /* ---------- 纯文本 ---------- */
                    case TextContent txt:
                        if (pendingText is null)
                        {
                            pendingText = new VllmChatRequestMessage
                            {
                                Role = msg.Role.Value,
                                Content = txt.Text
                            };
                        }
                        else
                        {
                            pendingText.Content += txt.Text;    // 多段文本拼在一起
                        }
                        break;

                    /* ---------- 工具调用 ---------- */
                    case FunctionCallContent fcc:
                        {
                            // 把调用指令格式化为模板 2 需要的两行
                            var requestContent = new VllmChatRequestMessage
                            {
                                Role = "assistant",
                                Content = JsonSerializer.Serialize(new VllmFunctionCallContent
                                {
                                    CallId = fcc.CallId,
                                    Name = fcc.Name,
                                    Arguments = JsonSerializer.SerializeToElement(fcc.Arguments, ToolCallJsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object?>))),
                                }, JsonContext.Default.VllmFunctionCallContent)
                            };
                            if (pendingText is not null)
                            {
                                yield return requestContent;
                                pendingText = null;
                            }
                            break;
                        }

                    /* ---------- 工具结果 ---------- */
                    case FunctionResultContent frc:
                        {
                            if (pendingText is not null)
                            {
                                yield return pendingText;
                                pendingText = null;
                            }

                            JsonElement jsonResult = JsonSerializer.SerializeToElement(frc.Result, ToolCallJsonSerializerOptions.GetTypeInfo(typeof(object)));

                            yield return new VllmChatRequestMessage
                            {
                                Role = "observation",
                                Content = jsonResult.ToString()
                            };
                            break;
                        }

                    /* ---------- 图片等二进制 ---------- */
                    case DataContent data when data.HasTopLevelMediaType("image"):
                        {
                            IList<string> images = pendingText?.Images ?? new List<string>();
#if NET
                            images.Add(Convert.ToBase64String(data.Data.Span));
#else
                images.Add(Convert.ToBase64String(data.Data.ToArray()));
#endif
                            if (pendingText is not null)
                            {
                                pendingText.Images = images;
                            }
                            else
                            {
                                yield return new VllmChatRequestMessage
                                {
                                    Role = msg.Role.Value,
                                    Images = images
                                };
                            }
                            break;
                        }
                }
            }

            // 循环结束后还有缓存文本就输出
            if (pendingText is not null)
                yield return pendingText;
        }


        /// <summary>
        /// 将 AIFunction 对象转换为 VllmTool 对象，用于支持工具调用功能
        /// </summary>
        private static VllmTool ToVllmTool(AIFunction function)
        {
            return new()
            {
                Type = "function",
                Function = new VllmFunctionTool
                {
                    Name = function.Name,
                    Description = function.Description,
                    Parameters = JsonSerializer.Deserialize(function.JsonSchema, JsonContext.Default.VllmFunctionToolParameters)!,
                }
            };
        }
    }
}
