﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Shared.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;


namespace Microsoft.Extensions.AI
{

    public sealed class VllmGemmaChatClient : IChatClient
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
        public VllmGemmaChatClient(string endpoint, string? token = null, string? modelId = null, HttpClient? httpClient = null)
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

            return new(FromVllmMessage(response.Choices.FirstOrDefault()?.Message!))
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
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
            string buff_toolcall = string.Empty;
            bool closeBuff = false;
            var funcList = new List<VllmFunctionToolCall>();
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
                    // 在流式结束时，若 buffer_msg 以 <tool_call 开头但未闭合，则丢弃，不输出为文本
                    if (!string.IsNullOrWhiteSpace(buffer_msg) && buffer_msg.TrimStart().StartsWith("<tool_call") && !buffer_msg.Contains("</tool_call>"))
                    {
                        buffer_msg = string.Empty;
                    }
                    break;
                }

                var chunk = JsonSerializer.Deserialize(jsonPart, JsonContext.Default.VllmChatStreamResponse);
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
                    Role = chunk.Choices.FirstOrDefault()?.Delta.Role is not null ? new ChatRole(chunk.Choices.FirstOrDefault()?.Delta.Role) : null,
                };

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
                {
                    buffer_msg += message.Content ?? string.Empty;
                    var buffer_copy = buffer_msg;
                    

                    // A) 已闭合的 <tool_call>…
                    ToolcallParser.TryFlushClosedToolCallBlocks(ref buffer_copy, out var tcalls);
                    funcList.AddRange(tcalls);

                    // B) 连写 JSON
                    var (jsonPieces, rest) = ToolcallParser.SliceJsonFragments(buffer_copy);
                    buffer_copy = rest;
                    foreach (var json in jsonPieces)
                    {
                        if (ToolcallParser.TryParseToolCallJson(json) is { } call)
                            funcList.Add(call);
                        buffer_copy += json ?? "";
                    }
                    // C) 有工具调用 ⇒ 推送并继续读取后续 chunk
                    if (funcList.Count > 0)
                    {
                        buffer_copy = string.Empty; // 清空已消费部分
                        break;                  // 继续 while，等待 DONE 或最终文本
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
                        if(buff_toolcall.Length> 0 && buff_toolcall.Length < 11)
                        {
                            buff_toolcall += message.Content;
                            continue;
                        }
                        yield return BuildTextUpdate(responseId, message.Content);
                    }
                }
            }

            if(funcList.Count>0)
            {
                foreach (var func in funcList)
                {
                    yield return BuildToolCallUpdate(responseId, func);
                }
            }
        }

        private ChatResponseUpdate BuildTextUpdate(
            string responseId, string text)
        {
            return new ReasoningChatResponseUpdate
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
                return new ChatMessage(new ChatRole(message.Role), contents);


            var raw = message.Content;
            var tcList = ToolcallParser.ParseToolCalls(raw, out var afterToolCalls);

            foreach (var call in tcList)
                contents.Add(ToFunctionCallContent(call));

            // 连写 JSON 切片
            var (jsonPieces, rest) = ToolcallParser.SliceJsonFragments(afterToolCalls);

            foreach (var json in jsonPieces)
            {
                var call = ToolcallParser.TryParseToolCallJson(json);
                if (call != null)
                    contents.Add(ToFunctionCallContent(call));
                else
                    contents.Add(new TextContent(json));
            }

            // 纯文本
            rest = rest.Trim();
            if (!string.IsNullOrEmpty(rest))
                contents.Add(new TextContent(rest));

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
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(function.Arguments);
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
            var msgs = messages.SelectMany(ToVllmChatRequestMessages).ToArray();
            var msgContent = JsonSerializer.Serialize(msgs);
            VllmOpenAIChatRequest request = new()
            {
                Format = ToVllmChatResponseFormat(options?.ResponseFormat),
                Messages = msgs,
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
        private IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            // 通常，我们对每个理解的内容项返回一个请求消息。
            // 然而，各种图像模型期望同一个请求消息中同时包含文本和图像。
            // 为此，如果存在文本消息，则将图像附加到之前的文本消息上。

            var parts = new List<Dictionary<string, object>>();

            foreach (var item in content.Contents)
            {
                switch (item)
                {
                    case TextContent textContent:
                        parts.Add(new Dictionary<string, object>
                {
                    { "type", "text" },
                    { "text", textContent.Text }
                });
                        break;

                    case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                        var base64 = Convert.ToBase64String(dataContent.Data
#if NET
                       .Span);
#else
                       .ToArray());
#endif
                        var imgUrl = $"data:{dataContent.MediaType};base64,{base64}";
                        parts.Add(new Dictionary<string, object>
                {
                    { "type", "image_url" },
                    { "image_url", new { url = imgUrl } }
                });
                        break;

                    case FunctionCallContent fcc:
                        yield return new VllmOpenAIChatRequestMessage
                        {
                            Role = "assistant",
                            Content = null,
                            ToolCalls = new[] {
                                new VllmToolCall {
                                    Id = fcc.CallId,
                                    Type = "function",
                                    Function = new VllmFunctionToolCall {
                                        Name = fcc.Name,
                                        Arguments = JsonSerializer.Serialize(
                                            fcc.Arguments,
                                            ToolCallJsonSerializerOptions
                                                .GetTypeInfo(typeof(IDictionary<string, object?>)))
                                    }
                                }
                            }
                        };
                        break;

                    case FunctionResultContent frc:
                        string resultStr = frc.Result is string s
                            ? s
                            : JsonSerializer.Serialize(frc.Result,
                                  ToolCallJsonSerializerOptions.GetTypeInfo(typeof(object)));

                        yield return new VllmOpenAIChatRequestMessage
                        {
                            Role = "tool",
                            Name = "", // 可选
                            ToolCallId = frc.CallId,
                            Content = resultStr
                        };
                        break;
                }
            }

            if (parts.Count > 0)
            {
                yield return new VllmOpenAIChatRequestMessage
                {
                    Role = content.Role.Value,
                    Content = JsonSerializer.Serialize(parts)
                };
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
