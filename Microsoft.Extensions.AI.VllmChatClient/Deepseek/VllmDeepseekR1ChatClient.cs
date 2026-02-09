using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Shared.Diagnostics;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// VllmDeepseekR1ChatClient - 针对 DeepSeek R1 模型的 ChatClient 实现
    /// 继承自 VllmBaseChatClient，重写流式响应处理以支持 ReasoningContent
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
        /// 重写流式响应处理，以支持 DeepSeek R1 的 ReasoningContent 格式
        /// </summary>
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, 
            ChatOptions? options = null, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(messages);
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

                if (chunk == null || chunk.Choices.Count == 0)
                {
                    continue;
                }
                
                string? modelId = chunk.Model ?? Metadata.DefaultModelId;

                ReasoningChatResponseUpdate update = new()
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                    FinishReason = ToFinishReason(chunk.Choices.FirstOrDefault()?.FinishReason),
                    ModelId = modelId,
                    ResponseId = responseId,
                    Thinking = true,
                    Role = chunk.Choices.FirstOrDefault()?.Delta?.Role is not null 
                        ? new ChatRole(chunk.Choices.FirstOrDefault()?.Delta?.Role) 
                        : null,
                };

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
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
                    else
                    {
                        update.Contents.Insert(0, new TextContent(message.ReasoningContent));
                    }
                }
                
                update.Thinking = thinking;
                yield return update;
            }
        }
    }
}
