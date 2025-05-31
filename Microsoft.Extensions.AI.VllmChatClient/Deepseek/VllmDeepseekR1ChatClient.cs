using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    public sealed class VllmDeepseekR1ChatClient : IChatClient
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
        public VllmDeepseekR1ChatClient(string endpoint, string? token = null, string? modelId = "deepseekr1", HttpClient? httpClient = null)
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

            if (response.Choices.Length == 0)
            {
                throw new InvalidOperationException("未返回任何响应选项。");
            }

            return new(FromVllmMessage(response.Choices.FirstOrDefault()?.Message!))
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? _metadata.ModelId,
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

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // 检查 messages 参数是否为 null
            _ = Throw.IfNull(messages);
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
            string anwser_msg = string.Empty;
            bool thinking = true;
            bool printThink = false;
            yield return new ReasoningChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = null,
                ModelId = options?.ModelId ?? _metadata.ModelId,
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

                if (chunk == null || chunk.Choices.Count == 0)
                {
                    continue;
                }
                string? modelId = chunk.Model ?? _metadata.ModelId;

                ReasoningChatResponseUpdate update = new()
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                    FinishReason = ToFinishReason(chunk.Choices.FirstOrDefault()?.FinishReason),
                    ModelId = modelId,
                    ResponseId = responseId,
                    Thinking = true,
                    Role = chunk.Choices.FirstOrDefault()?.Delta.Role is not null ? new ChatRole(chunk.Choices.FirstOrDefault()?.Delta?.Role) : null,
                };

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
                {
                    
                    if(message.Content?.Length > 0)
                    {
                        if(printThink == false) 
                        {
                            
                            var textContent = $"\n</think>\n";
                            printThink = true;
                            anwser_msg = message.Content;
                            update.Contents.Insert(0, new TextContent(textContent));
                            update.Thinking = thinking;
                            yield return update;
                            thinking = false;
                            continue;
                        }
                        else
                        {
                            if(anwser_msg.Length > 0)
                            {
                                update.Contents.Insert(0, new TextContent(anwser_msg+message.Content));
                                anwser_msg = string.Empty;
                            }else
                            {
                                update.Contents.Insert(0, new TextContent(message.Content));
                            }
                            
                        }
                        
                    }
                    else
                    {
                        update.Contents.Insert(0,new TextContent(message.ReasoningContent));
                    }
                }
                update.Thinking = thinking;
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
        private static ChatFinishReason? ToFinishReason(string reason) =>
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
        private static ChatMessage FromVllmMessage(VllmChatResponseMessage message)
        {
            List<AIContent> contents = [];

            if (message.Content?.Length > 0 || contents.Count == 0)
            {
                contents.Insert(0, new TextContent(message.Content));
            }

            return new ChatMessage(new(message.Role), contents);
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
            var arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(function.Arguments);
            return new FunctionCallContent(id, function.Name, arguments);
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
                Model = options?.ModelId ?? _metadata.ModelId ?? string.Empty,
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
        private IEnumerable<VllmChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            // 通常，我们对每个理解的内容项返回一个请求消息。
            // 然而，各种图像模型期望同一个请求消息中同时包含文本和图像。
            // 为此，如果存在文本消息，则将图像附加到之前的文本消息上。

            VllmChatRequestMessage? currentTextMessage = null;
            foreach (var item in content.Contents)
            {
                if (item is DataContent dataContent && dataContent.HasTopLevelMediaType("image"))
                {
                    IList<string> images = currentTextMessage?.Images ?? [];
                    images.Add(Convert.ToBase64String(dataContent.Data
#if NET
                        .Span));
#else
                        .ToArray()));
#endif

                    if (currentTextMessage is not null)
                    {
                        currentTextMessage.Images = images;
                    }
                    else
                    {
                        yield return new VllmChatRequestMessage
                        {
                            Role = content.Role.Value,
                            Images = images,
                        };
                    }
                }
                else
                {
                    if (currentTextMessage is not null)
                    {
                        yield return currentTextMessage;
                        currentTextMessage = null;
                    }

                    switch (item)
                    {
                        case TextContent textContent:
                            currentTextMessage = new VllmChatRequestMessage
                            {
                                Role = content.Role.Value,
                                Content = textContent.Text,
                            };
                            break;

                        case FunctionCallContent fcc:
                            {
                                yield return new VllmChatRequestMessage
                                {
                                    Role = "assistant",
                                    Content = JsonSerializer.Serialize(new VllmFunctionCallContent
                                    {
                                        CallId = fcc.CallId,
                                        Name = fcc.Name,
                                        Arguments = JsonSerializer.SerializeToElement(fcc.Arguments, ToolCallJsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object?>))),
                                    }, JsonContext.Default.VllmFunctionCallContent)
                                };
                                break;
                            }

                        case FunctionResultContent frc:
                            {
                                JsonElement jsonResult = JsonSerializer.SerializeToElement(frc.Result, ToolCallJsonSerializerOptions.GetTypeInfo(typeof(object)));
                                yield return new VllmChatRequestMessage
                                {
                                    Role = "tool",
                                    Content = JsonSerializer.Serialize(new VllmFunctionResultContent
                                    {
                                        CallId = frc.CallId,
                                        Result = jsonResult,
                                    }, JsonContext.Default.VllmFunctionResultContent)
                                };
                                break;
                            }
                    }
                }
            }

            if (currentTextMessage is not null)
            {
                yield return currentTextMessage;
            }
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
