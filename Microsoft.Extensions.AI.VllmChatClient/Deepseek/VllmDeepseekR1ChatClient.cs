using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// VllmDeepseekR1ChatClient - 针对 DeepSeek R1 模型的 ChatClient 实现
    /// 继承自 VllmBaseChatClient，重写流式响应处理以支持 ReasoningContent 和工具调用
    /// </summary>
    public sealed class VllmDeepseekR1ChatClient : VllmBaseChatClient
    {
        /// <summary>初始化 VllmDeepseekR1ChatClient 类的新实例</summary>
        /// <param name="endpoint">vllm 服务托管的端点 URI。格式 "http://localhost:8000/{0}/{1}"</param>
        /// <param name="token">API 认证令牌</param>
        /// <param name="modelId">要使用的模型的 ID</param>
        /// <param name="httpClient">用于 HTTP 操作的 HttpClient 实例</param>
        public VllmDeepseekR1ChatClient(string endpoint, string? token = null, string? modelId = "deepseekr1", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        /// <summary>
        /// 重写请求构建，支持 VllmChatOptions 的思维链开关
        /// </summary>
        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);
            
            // 支持 VllmChatOptions 及其派生类的思维链开关
            if (options is VllmChatOptions vllmOptions)
            {
                request.Thinking = new VllmThinkingOptions
                {
                    Type = vllmOptions.ThinkingEnabled ? "enabled" : "disabled"
                };
            }

            return request;
        }



        /// <summary>
        /// 重写流式响应处理，支持 DeepSeek R1 的 ReasoningContent 格式和工具调用
        /// 包含自动检测工具结果并发起后续请求的逻辑
        /// </summary>
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, 
            ChatOptions? options = null, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(messages);
            
            var messagesList = messages as IList<ChatMessage>;

            bool continueLoop;
            do
            {
                continueLoop = false;
                bool hasToolCalls = false;
                int messageCountBefore = messagesList?.Count ?? -1;

                await foreach (var update in StreamSingleResponseAsync(messages, options, cancellationToken))
                {
                    if (update.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        hasToolCalls = true;
                    }
                    yield return update;
                }

                // 自动检测工具结果并发起后续请求
                if (hasToolCalls &&
                    messagesList is not null &&
                    messagesList.Count > messageCountBefore &&
                    messagesList[messagesList.Count - 1].Role == ChatRole.Tool)
                {
                    continueLoop = true;
                }
            } while (continueLoop);
        }

        /// <summary>
        /// 单次流式响应处理（内部实现）
        /// </summary>
        private async IAsyncEnumerable<ChatResponseUpdate> StreamSingleResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string apiEndpoint = GetChatEndpoint();
            
            using HttpRequestMessage request = new(HttpMethod.Post, apiEndpoint)
            {
                Content = JsonContent.Create(ToVllmChatRequest(messages, options, stream: true), JsonContext.Default.VllmOpenAIChatRequest)
            };
            
            using var httpResponse = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            var responseId = Guid.NewGuid().ToString("N");

            using var httpResponseStream = await httpResponse.Content
#if NET
                .ReadAsStreamAsync(cancellationToken)
#else
                .ReadAsStreamAsync()
#endif
                .ConfigureAwait(false);

            using var streamReader = new StreamReader(httpResponseStream);
            string answerMsg = string.Empty;
            bool thinking = true;
            bool printThink = false;
            
            // 工具调用缓冲
            string bufferName = string.Empty;
            string bufferParams = string.Empty;
            string? bufferCallId = null;
            
            // 发送初始思考标记
            yield return new ReasoningChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = null,
                ModelId = options?.ModelId ?? Metadata.DefaultModelId,
                ResponseId = responseId,
                Thinking = true,
                Role = ChatRole.Assistant,
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
                
                string? modelId = chunk.Model ?? Metadata.DefaultModelId;
                var choice = chunk.Choices.FirstOrDefault();
                var delta = choice?.Delta;

                // 处理工具调用
                if (delta?.ToolCalls?.Length > 0)
                {
                    var toolCall = delta.ToolCalls.FirstOrDefault();
                    if (toolCall != null)
                    {
                        if (!string.IsNullOrEmpty(toolCall.Id))
                        {
                            bufferCallId = toolCall.Id;
                        }
                        if (!string.IsNullOrEmpty(toolCall.Function?.Name))
                        {
                            bufferName = toolCall.Function.Name;
                        }
                        bufferParams += toolCall.Function?.Arguments ?? "";

                        // 检查 JSON 是否完整
                        bool isJsonComplete = false;
                        try
                        {
                            if (!string.IsNullOrEmpty(bufferName) && !string.IsNullOrEmpty(bufferParams))
                            {
                                _ = JsonConvert.DeserializeObject(bufferParams);
                                isJsonComplete = ToolcallParser.GetBraceDepth(bufferParams) == 0;
                            }
                        }
                        catch { }

                        if (isJsonComplete)
                        {
                            yield return BuildToolCallUpdateWithId(responseId, bufferCallId, bufferName, bufferParams);
                            bufferName = string.Empty;
                            bufferParams = string.Empty;
                            bufferCallId = null;
                        }
                    }
                    continue;
                }

                // 检查是否为工具调用结束
                if (choice?.FinishReason == "tool_calls")
                {
                    // 如果还有未发送的工具调用，尝试发送
                    if (!string.IsNullOrEmpty(bufferName) && !string.IsNullOrEmpty(bufferParams))
                    {
                        yield return BuildToolCallUpdateWithId(responseId, bufferCallId, bufferName, bufferParams);
                        bufferName = string.Empty;
                        bufferParams = string.Empty;
                        bufferCallId = null;
                    }
                    continue;
                }

                ReasoningChatResponseUpdate update = new()
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                    FinishReason = ToFinishReason(choice?.FinishReason),
                    ModelId = modelId,
                    ResponseId = responseId,
                    Thinking = true,
                    Role = delta?.Role is not null 
                        ? new ChatRole(delta.Role) 
                        : null,
                };

                if (delta is { } message)
                {
                    if (message.Content?.Length > 0)
                    {
                        if (!printThink)
                        {
                            var textContent = "\n</think>\n";
                            printThink = true;
                            answerMsg = message.Content;
                            update.Contents.Insert(0, new TextContent(textContent));
                            update.Thinking = thinking;
                            yield return update;
                            thinking = false;
                            continue;
                        }
                        else
                        {
                            if (answerMsg.Length > 0)
                            {
                                update.Contents.Insert(0, new TextContent(answerMsg + message.Content));
                                answerMsg = string.Empty;
                            }
                            else
                            {
                                update.Contents.Insert(0, new TextContent(message.Content));
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(message.ReasoningContent))
                    {
                        update.Contents.Insert(0, new TextContent(message.ReasoningContent));
                    }
                    else
                    {
                        continue; // 跳过空内容
                    }
                }
                else
                {
                    continue;
                }
                
                update.Thinking = thinking;
                yield return update;
            }
        }

        /// <summary>
        /// 构建带 CallId 的工具调用更新
        /// </summary>
        private ChatResponseUpdate BuildToolCallUpdateWithId(string responseId, string? callId, string name, string arguments)
        {
            var functionCall = new VllmFunctionToolCall
            {
                Name = name,
                Arguments = arguments
            };

            var fcc = ToFunctionCallContentWithId(callId, functionCall);

            return new ChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = ChatFinishReason.ToolCalls,
                ModelId = Metadata.DefaultModelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { fcc }
            };
        }

        /// <summary>
        /// 使用指定的 CallId 创建 FunctionCallContent
        /// </summary>
        private static FunctionCallContent ToFunctionCallContentWithId(string? callId, VllmFunctionToolCall function)
        {
            var id = callId ?? 
#if NET
                System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                Guid.NewGuid().ToString().Substring(0, 8);
#endif
            var arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(function.Arguments ?? "{}");
            return new FunctionCallContent(id, function.Name ?? "", arguments);
        }
    }
}
