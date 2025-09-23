﻿using Microsoft.Shared.Diagnostics;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    public class VllmQwen3NextChatClient : IChatClient
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
        public VllmQwen3NextChatClient(string endpoint, string? token = null, string? modelId = "qwen3", HttpClient? httpClient = null)
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
                JsonContext.Default.VllmOpenAIChatRequest,
                cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            //var test = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var response = (await httpResponse.Content.ReadFromJsonAsync(
                JsonContext.Default.VllmChatResponse,
                cancellationToken).ConfigureAwait(false))!;

            if (response.Choices.Length == 0)
            {
                throw new InvalidOperationException("未返回任何响应选项。");
            }

            var ret = new ReasoningChatResponse(FromVllmMessage(response.Choices.FirstOrDefault()?.Message!), response.Choices.FirstOrDefault()?.Message.ReasoningContent?.ToString())
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseVllmChatResponseUsage(response),
                
            };
            return ret;
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
                Content = JsonContent.Create(ToVllmChatRequest(messages, options, stream: true), JsonContext.Default.VllmOpenAIChatRequest)
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
            string buffer_name = string.Empty;
            string buffer_params = string.Empty;
            bool insideThink = false;   // 是否正在 <think> 中
            bool insideToolCall = false;   // 是否正在 <tool_call> 中（用于兜底残缺情况）
            string buff_toolcall = string.Empty;
            yield return new ChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = null,
                ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = responseId,
                Role = new ChatRole("assistant"),
                Contents = new List<AIContent>(),
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
                string? modelId = chunk.Model ?? _metadata.DefaultModelId;

                if (chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.Length == 1)
                {
                    if (string.IsNullOrEmpty(buffer_name))
                    {
                        buffer_name = chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.FirstOrDefault()?.Function?.Name ?? "";
                    }
                    buffer_params += chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.FirstOrDefault()?.Function?.Arguments?.ToString() ?? "";
                }

                ChatResponseUpdate update = new()
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                    FinishReason = ToFinishReason(chunk.Choices.FirstOrDefault()?.FinishReason),
                    ModelId = modelId,
                    ResponseId = responseId,
                    Role = chunk.Choices.FirstOrDefault()?.Delta.Role is not null ? new ChatRole(chunk.Choices.FirstOrDefault()?.Delta?.Role) : null,
                };

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
                {
                    buffer_msg += message.Content ?? string.Empty;
                    var buffer_copy = buffer_msg;
                    var funcList = new List<VllmFunctionToolCall>();

                    foreach(var call in message.ToolCalls ?? [])
                    {
                        bool isJsonComplete = false;
                        try
                        {
                            if (!string.IsNullOrEmpty(buffer_name))
                            {
                                var obj = JsonConvert.DeserializeObject(buffer_params);
                                isJsonComplete = ToolcallParser.GetBraceDepth(buffer_params) == 0;
                            }
                            
                        }
                        catch (Exception)
                        {
                        }

                        if (isJsonComplete)
                        {
                            funcList.Add( new VllmFunctionToolCall
                            {
                                Name = buffer_name,
                                Arguments = buffer_params
                            });
                            buffer_params = string.Empty;
                            buffer_name = string.Empty;
                        }
                    }

                    if (funcList.Count > 0)
                    {
                        foreach (var call in funcList)
                            yield return BuildToolCallUpdate(responseId, call);

                        buffer_copy = string.Empty; // 清空已消费部分
                        continue;                  // 继续 while，等待 DONE 或最终文本
                    }

                    // D) 普通文本
                    //bool jsonIncomplete = ToolcallParser.GetBraceDepth(buffer_copy) > 0;
                    bool inToolCallBlock = ToolcallParser.IsInsideIncompleteToolCall(buffer_copy);
                    bool isUnclosed = ToolcallParser.HasUnclosedToolCall(buffer_msg);

                    if (!inToolCallBlock && !isUnclosed &&
                        funcList.Count == 0 &&                       // 本帧未输出工具调用
                        !string.IsNullOrEmpty(message.Content))
                    {
                        if (message.Content == "<")
                        {
                            buff_toolcall += message.Content;
                        }
                        if (buff_toolcall.Length > 0 && buff_toolcall.Length < 11)
                        {
                            buff_toolcall += message.Content;
                            continue;
                        }
                        yield return BuildTextUpdate(responseId, message.Content);
                    }
                }

            }
        }



        private ChatResponseUpdate BuildTextUpdate(
        string responseId, string text)
        {
            return new ChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                ModelId = _metadata.DefaultModelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { new TextContent(text) }
            };
        }


        private ChatResponseUpdate BuildToolCallUpdate(string responseId, VllmFunctionToolCall call)
        {
            return new ChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = ChatFinishReason.ToolCalls,
                ModelId = _metadata.DefaultModelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { ToFunctionCallContent(call) }
            };
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
            var contents = new List<AIContent>();

            if (string.IsNullOrEmpty(message.Content))
            {
                foreach(var toolcall in message.ToolCalls ?? [])
                {
                    contents.Add(ToFunctionCallContent(new VllmFunctionToolCall
                    {
                        Name = toolcall.Function?.Name ?? "",
                        Arguments = toolcall.Function?.Arguments?.ToString() ?? "{}"
                    }));
                }
            }

            // ① 去掉 <think> 标记
            if (message.Content != null)
            {
                var raw = message.Content;

                var tcList = ToolcallParser.ParseToolCalls(raw, out var afterToolCalls);

                foreach (var call in tcList)
                    contents.Add(ToFunctionCallContent(call));
                // ③ 连写 JSON 切片
                var (jsonPieces, rest) = ToolcallParser.SliceJsonFragments(afterToolCalls);

                foreach (var json in jsonPieces)
                {
                    var call = ToolcallParser.TryParseToolCallJson(json);
                    if (call != null)
                        contents.Add(ToFunctionCallContent(call));
                }
                if (contents.Count == 0)
                    rest = raw;
                // ④ 纯文本
                rest = rest.Trim();
                if (!string.IsNullOrEmpty(rest))
                    contents.Add(new TextContent(rest));
            }
            return new ChatMessage(new ChatRole(message.Role), contents);
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
        private VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            VllmOpenAIChatRequest request = new()
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
        /// <summary>
        /// 将 ChatMessage 对象转换为一个或多个 VllmOpenAIChatRequestMessage 消息
        /// </summary>
        private IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            // 通常，我们对每个理解的内容项返回一个请求消息。
            // 然而，各种图像模型期望同一个请求消息中同时包含文本和图像。
            // 为此，如果存在文本消息，则将图像附加到之前的文本消息上。

            VllmOpenAIChatRequestMessage? currentTextMessage = null;
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
                        yield return new VllmOpenAIChatRequestMessage
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
                            currentTextMessage = new VllmOpenAIChatRequestMessage
                            {
                                Role = content.Role.Value,
                                Content = textContent.Text,
                            };
                            break;

                        case FunctionCallContent fcc:
                            {
                                // 使用 <tool_call> XML 标签格式，符合模板要求
                                var toolCallJson = JsonSerializer.Serialize(new
                                {
                                    name = fcc.Name,
                                    arguments = fcc.Arguments
                                }, ToolCallJsonSerializerOptions);

                                yield return new VllmOpenAIChatRequestMessage
                                {
                                    Role = "assistant",
                                    Content = $"<tool_call>\n{toolCallJson}\n</tool_call>",
                                };
                                break;
                            }

                        case FunctionResultContent frc:
                            {
                                // 使用 <tool_response> XML 标签格式，符合模板要求
                                var resultContent = frc.Result?.ToString() ?? "";
                                yield return new VllmOpenAIChatRequestMessage
                                {
                                    Role = "user",
                                    Content = $"<tool_response>\n{resultContent}\n</tool_response>",
                                    ToolCallId = frc.CallId
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
