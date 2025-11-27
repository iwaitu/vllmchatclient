using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI.VllmChatClient.Gemma
{

    public class VllmGemini3ChatClient : IChatClient
    {
        // 维护函数调用ID到函数名的映射，用于在发送 FunctionResponse 时恢复函数名
        [ThreadStatic]
        private static Dictionary<string,string>? _functionCallNameMap;

        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;

        /// <summary>关于客户端的元数据</summary>
        private readonly ChatClientMetadata _metadata;

        /// <summary>api/chat 端点 URI</summary>
        private readonly string _apiChatEndpoint;

        /// <summary>用于发送请求的 HttpClient 对象</summary>
        private readonly HttpClient _httpClient;

        /// <summary>标识是否使用 Gemini 原生 API</summary>
        private readonly bool _useGeminiNativeApi;

        /// <summary>
        /// Provides the default <see cref="JsonSerializerOptions"/> used for tool call serialization.
        /// </summary>
        /// <remarks>This field is initialized with the default options from <see
        /// cref="AIJsonUtilities.DefaultOptions"/>. It is intended for internal use when serializing or deserializing
        /// tool call payloads.</remarks>
        private JsonSerializerOptions _toolCallJsonSerializerOptions = AIJsonUtilities.DefaultOptions;
        public VllmGemini3ChatClient(string endpoint, string? token = null, string? modelId = "gemini-3-pro-preview", HttpClient? httpClient = null)
        {
            _ = Throw.IfNull(endpoint);
            if (modelId is not null)
            {
                _ = Throw.IfNullOrWhitespace(modelId);
            }

            // 确保 endpoint 以正确的格式结束
            if (endpoint.EndsWith("/"))
            {
                endpoint = endpoint.TrimEnd('/');
            }

            // 检查是否使用 Gemini 原生 API
            _useGeminiNativeApi = endpoint.Contains("generativelanguage.googleapis.com") ||
                                  endpoint.Contains(":generateContent") ||
                                  endpoint.Contains(":streamGenerateContent");

            // Gemini API 使用不同的端点格式：
            // https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
            
            // 检查是否已经是完整的 Gemini API URL
            if (endpoint.Contains(":generateContent") || endpoint.Contains(":streamGenerateContent"))
            {
                // 已经是完整 URL，直接使用
                _apiChatEndpoint = endpoint;
            }
            // 检查是否是 v1beta 基础 URL
            else if (endpoint.Contains("/v1beta") || endpoint.Contains("/v1alpha"))
            {
                // 构建 Gemini API 端点: /v1beta/models/{model}:generateContent
                _apiChatEndpoint = $"{endpoint}/models/{modelId}:generateContent";
            }
            // 兼容旧的 OpenAI 格式配置（用于向后兼容）
            else if (endpoint.Contains("/chat/completions"))
            {
                _apiChatEndpoint = endpoint;
            }
            else if (endpoint.Contains("/v1"))
            {
                // 如果已经包含 /v1，直接添加 /chat/completions
                _apiChatEndpoint = endpoint + "/chat/completions";
            }
            else
            {
                // 默认格式，使用占位符
                _apiChatEndpoint = endpoint + "/{0}/{1}";
            }

            _httpClient = httpClient ?? VllmUtilities.SharedClient;

            // Gemini API 使用 x-goog-api-key 头进行认证，而不是 Bearer token
            if (!string.IsNullOrEmpty(token))
            {
                // 移除可能存在的旧 Authorization 头
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                
                // 设置 Gemini API 特定的认证头
                _httpClient.DefaultRequestHeaders.Remove("x-goog-api-key");
                _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", token);
            }

            // 确定元数据使用的 URI
            string metadataUri;
            if (_apiChatEndpoint.Contains("{0}"))
            {
                metadataUri = string.Format(_apiChatEndpoint, "v1", "chat/completions");
            }
            else if (_apiChatEndpoint.Contains(":generateContent"))
            {
                // 对于 Gemini API，使用基础 endpoint
                metadataUri = _apiChatEndpoint;
            }
            else
            {
                metadataUri = _apiChatEndpoint;
            }

            _metadata = new("gemini", new Uri(metadataUri), modelId);
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

            // 确定 API 端点
            string apiEndpoint;
            if (_apiChatEndpoint.Contains(":generateContent") || _apiChatEndpoint.Contains(":streamGenerateContent"))
            {
                // 已经是 Gemini 原生端点，直接使用
                apiEndpoint = _apiChatEndpoint.Replace(":streamGenerateContent", ":generateContent");
            }
            else if (_apiChatEndpoint.Contains("{0}"))
            {
                // 使用占位符格式
                apiEndpoint = string.Format(_apiChatEndpoint, "v1", "chat/completions");
            }
            else
            {
                // 直接使用配置的端点
                apiEndpoint = _apiChatEndpoint;
            }

            HttpResponseMessage httpResponse;

            // 根据是否使用 Gemini 原生 API 选择不同的请求格式
            if (_useGeminiNativeApi)
            {
                // 使用 Gemini 原生 API 格式
                var geminiRequest = ToGeminiRequest(messages, options);
                
                httpResponse = await _httpClient.PostAsJsonAsync(
                    apiEndpoint,
                    geminiRequest,
                    cancellationToken).ConfigureAwait(false);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                }

                var geminiResponse = await httpResponse.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken).ConfigureAwait(false);
                
                if (geminiResponse == null)
                {
                    throw new InvalidOperationException("Gemini API 返回空响应。");
                }

                return FromGeminiResponse(geminiResponse, options);
            }
            else
            {
                // 使用 OpenAI 兼容格式（向后兼容）
                httpResponse = await _httpClient.PostAsJsonAsync(
                    apiEndpoint,
                    ToVllmChatRequest(messages, options, stream: false),
                    JsonContext.Default.VllmOpenAIChatRequest,
                    cancellationToken).ConfigureAwait(false);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                }

                var response = (await httpResponse.Content.ReadFromJsonAsync(
                    JsonContext.Default.VllmChatResponse,
                    cancellationToken).ConfigureAwait(false))!;

                if (response.Choices.Length == 0)
                {
                    throw new InvalidOperationException("未返回任何响应选项。");
                }

                var responseMessage = response.Choices.FirstOrDefault()?.Message;

                return new ReasoningChatResponse(FromVllmMessage(response.Choices.FirstOrDefault()?.Message!), response.Choices.FirstOrDefault()?.Message?.Reasoning ?? "")
                {
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                    FinishReason = ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                    ModelId = response.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                    ResponseId = response.Id,
                    Usage = ParseVllmChatResponseUsage(response),
                };
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // 检查 messages 参数是否为 null
            _ = Throw.IfNull(messages);

            // 确定 API 端点
            string apiEndpoint;
            if (_apiChatEndpoint.Contains(":generateContent"))
            {
                // Gemini 流式端点：将 generateContent 替换为 streamGenerateContent
                apiEndpoint = _apiChatEndpoint.Replace(":generateContent", ":streamGenerateContent");
            }
            else if (_apiChatEndpoint.Contains(":streamGenerateContent"))
            {
                // 已经是流式端点
                apiEndpoint = _apiChatEndpoint;
            }
            else if (_apiChatEndpoint.Contains("{0}"))
            {
                // 使用占位符格式
                apiEndpoint = string.Format(_apiChatEndpoint, "v1", "chat/completions");
            }
            else
            {
                // 直接使用配置的端点
                apiEndpoint = _apiChatEndpoint;
            }

            // 根据是否使用 Gemini 原生 API 选择不同的请求格式
            if (_useGeminiNativeApi)
            {
                // 使用 Gemini 原生 API 格式（流式）
                // Gemini 流式 API 使用 Server-Sent Events (SSE) 格式
                var geminiRequest = ToGeminiRequest(messages, options);

                using HttpRequestMessage request = new(HttpMethod.Post, apiEndpoint)
                {
                    Content = JsonContent.Create(geminiRequest)
                };

                // 添加 alt=sse 参数以获取 SSE 流
                var uriBuilder = new UriBuilder(apiEndpoint);
                var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                query["alt"] = "sse";
                uriBuilder.Query = query.ToString();
                request.RequestUri = uriBuilder.Uri;

                using var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
                }

                var responseId = Guid.NewGuid().ToString("N");

                using var geminiHttpResponseStream = await httpResponse.Content
#if NET
                    .ReadAsStreamAsync(cancellationToken)
#else
                    .ReadAsStreamAsync()
#endif
                    .ConfigureAwait(false);

                using var geminiStreamReader = new StreamReader(geminiHttpResponseStream);
                string? lastThoughtSignature = null; // 追踪最近的思维签名供后续函数调用附加

#if NET
                while ((await geminiStreamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { } line)
#else
                while ((await geminiStreamReader.ReadLineAsync().ConfigureAwait(false)) is { } line)
#endif
                {
                    // 跳过空行和非数据行
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                    {
                        continue;
                    }

                    var jsonData = line.Substring(6); // 移除 "data: " 前缀

                    // 检查是否是结束标记
                    if (jsonData == "[DONE]" || string.IsNullOrWhiteSpace(jsonData))
                    {
                        continue;
                    }

                    GeminiResponse? geminiStreamChunk = null;
                    try
                    {
                        geminiStreamChunk = System.Text.Json.JsonSerializer.Deserialize<GeminiResponse>(jsonData);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // 忽略无法解析的 JSON块
                        continue;
                    }

                    if (geminiStreamChunk?.Candidates != null && geminiStreamChunk.Candidates.Length > 0)
                    {
                        var candidate = geminiStreamChunk.Candidates[0];
                        var content = candidate.Content;

                        if (content?.Parts != null)
                        {
                            foreach (var part in content.Parts)
                            {
                                string? signature = part.ThoughtSignature;
                                if (!string.IsNullOrEmpty(signature))
                                {
                                    lastThoughtSignature = signature; // 更新最近的签名
                                }

                                if (part.FunctionCall != null)
                                {
#if NET
                                    var callId = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                                    var callId = Guid.NewGuid().ToString().Substring(0, 8);
#endif
                                    var argsDict = part.FunctionCall.Args ?? new Dictionary<string, object?>();

                                    // 如果当前函数调用未携带签名但之前捕获到签名，则补充
                                    if (string.IsNullOrEmpty(signature) && !string.IsNullOrEmpty(lastThoughtSignature))
                                    {
                                        signature = lastThoughtSignature;
                                    }

                                    if (!string.IsNullOrEmpty(signature))
                                    {
                                        argsDict["thoughtSignature"] = signature;
                                    }

                                    var fcc = new FunctionCallContent(callId, part.FunctionCall.Name, argsDict);
                                    (_functionCallNameMap ??= new()).TryAdd(callId, part.FunctionCall.Name ?? string.Empty);

                                    yield return new ReasoningChatResponseUpdate
                                    {
                                        CreatedAt = DateTimeOffset.UtcNow,
                                        FinishReason = ChatFinishReason.ToolCalls,
                                        ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                                        ResponseId = responseId,
                                        Role = ChatRole.Assistant,
                                        Thinking = false,
                                        Reasoning = string.Empty,
                                        Contents = new List<AIContent> { fcc }
                                    };
                                }
                                else if (!string.IsNullOrEmpty(signature))
                                {
                                    // 单独的签名（没有函数调用），输出占位推理更新
                                    yield return new ReasoningChatResponseUpdate
                                    {
                                        CreatedAt = DateTimeOffset.UtcNow,
                                        FinishReason = null,
                                        ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                                        ResponseId = responseId,
                                        Role = ChatRole.Assistant,
                                        Thinking = true,
                                        Reasoning = signature,
                                        Contents = new List<AIContent> { new TextContent("[thoughtSignature]") }
                                    };
                                }

                                if (!string.IsNullOrEmpty(part.Text))
                                {
                                    yield return new ReasoningChatResponseUpdate
                                    {
                                        CreatedAt = DateTimeOffset.UtcNow,
                                        FinishReason = candidate.FinishReason == "STOP" ? ChatFinishReason.Stop : null,
                                        ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                                        ResponseId = responseId,
                                        Role = ChatRole.Assistant,
                                        Thinking = false,
                                        Reasoning = "",
                                        Contents = new List<AIContent> { new TextContent(part.Text) }
                                    };
                                }
                            }
                        }
                    }
                }

                yield break;
            }

            // 以下是 OpenAI 兼容格式的流式处理（原有代码）
            using HttpRequestMessage openaiRequest = new(HttpMethod.Post, apiEndpoint)
            {
                Content = JsonContent.Create(ToVllmChatRequest(messages, options, stream: true), JsonContext.Default.VllmOpenAIChatRequest)
            };

            using var openaiHttpResponse = await _httpClient.SendAsync(openaiRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!openaiHttpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(openaiHttpResponse, cancellationToken).ConfigureAwait(false);
            }

            // vllm 在流式传输时不会在每个数据块中设置响应 ID，因此我们需要生成一个
            var openaiResponseId = Guid.NewGuid().ToString("N");

            using var httpResponseStream = await openaiHttpResponse.Content
#if NET
                .ReadAsStreamAsync(cancellationToken)
#else
                .ReadAsStreamAsync()
#endif
                .ConfigureAwait(false);

            using var streamReader = new StreamReader(httpResponseStream);

            // 改进的工具调用状态管理
            var activeFunctionCalls = new Dictionary<int, FunctionCallState>();
            bool hasActiveToolCalls = false;
            ChatFinishReason? streamFinishReason = null;

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

                VllmChatStreamResponse? chunk = null;
                try
                {
                    chunk = System.Text.Json.JsonSerializer.Deserialize(jsonPart, JsonContext.Default.VllmChatStreamResponse);
                }
                catch (System.Text.Json.JsonException)
                {
                    // 跳过无法解析的JSON块
                    continue;
                }

                if (chunk == null)
                {
                    continue;
                }

                string? modelId = chunk.Model ?? _metadata.DefaultModelId;

                // 处理传统的 Chat Completions API 格式
                if (chunk.Choices == null || chunk.Choices.Count == 0)
                {
                    continue;
                }

                var choice = chunk.Choices.FirstOrDefault();
                if (choice?.Delta == null) continue;

                // 立即输出推理内容作为思考更新（传统格式）
                if (!string.IsNullOrEmpty(choice.Delta.Reasoning) ||
                    choice.Delta.ReasoningContent != null ||
                    choice.Delta.ReasoningDetails?.Length > 0)
                {
                    string reasoningText = "";
                    string reasoningType = "unknown";

                    // 优先使用 reasoning 字段（基于 Python 脚本输出）
                    if (!string.IsNullOrEmpty(choice.Delta.Reasoning))
                    {
                        reasoningText = choice.Delta.Reasoning;
                        reasoningType = "direct";
                    }
                    // 检查 reasoning_details 数组
                    else if (choice.Delta.ReasoningDetails?.Length > 0)
                    {
                        var reasoningDetail = choice.Delta.ReasoningDetails.FirstOrDefault(d => d.Type == "reasoning.text");
                        if (reasoningDetail != null && !string.IsNullOrEmpty(reasoningDetail.Text))
                        {
                            reasoningText = reasoningDetail.Text;
                            reasoningType = "structured";
                        }
                    }
                    // 回退到 reasoning_content 字段
                    else if (choice.Delta.ReasoningContent != null)
                    {
                        var (hasReasoning, extractedText, extractedType) = AnalyzeReasoningStructure(choice.Delta.ReasoningContent);
                        if (hasReasoning)
                        {
                            reasoningText = extractedText;
                            reasoningType = extractedType;
                        }
                    }

                    if (!string.IsNullOrEmpty(reasoningText))
                    {
                        yield return new ReasoningChatResponseUpdate
                        {
                            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                            FinishReason = null,
                            ModelId = modelId,
                            ResponseId = openaiResponseId,
                            Role = choice.Delta.Role is not null ? new ChatRole(choice.Delta.Role) : ChatRole.Assistant,
                            Thinking = true,
                            Reasoning = reasoningText,
                            Contents = new List<AIContent> { new TextContent(reasoningText) }
                        };
                    }
                }

                // 检查完成状态
                if (!string.IsNullOrEmpty(choice.FinishReason))
                {
                    streamFinishReason = ToFinishReason(choice.FinishReason);

                    // 如果流结束且有活跃的工具调用，输出它们
                    if (hasActiveToolCalls && activeFunctionCalls.Count > 0)
                    {
                        foreach (var kvp in activeFunctionCalls)
                        {
                            var state = kvp.Value;
                            if (!string.IsNullOrEmpty(state.Name))
                            {
                                yield return CreateToolCallUpdate(openaiResponseId, modelId, state);
                            }
                        }
                        activeFunctionCalls.Clear();
                        hasActiveToolCalls = false;
                    }
                }

                // 处理工具调用增量更新
                if (choice.Delta.ToolCalls?.Length > 0)
                {
                    hasActiveToolCalls = true;

                    foreach (var toolCall in choice.Delta.ToolCalls)
                    {
                        if (toolCall.Function != null)
                        {
                            int index = toolCall.Index ?? 0; // 使用 OpenAI 格式的 index 字段

                            // 初始化或更新工具调用状态
                            if (!activeFunctionCalls.ContainsKey(index))
                            {
                                activeFunctionCalls[index] = new FunctionCallState
                                {
                                    Id = toolCall.Id ?? $"call_{Guid.NewGuid().ToString("N")[..8]}",
                                    Name = "",
                                    Arguments = new StringBuilder()
                                };
                            }

                            var state = activeFunctionCalls[index];

                            // 累积函数名称（通常在第一个chunk中）
                            if (!string.IsNullOrEmpty(toolCall.Function.Name))
                            {
                                state.Name = toolCall.Function.Name;
                            }

                            // 累积参数
                            if (!string.IsNullOrEmpty(toolCall.Function.Arguments))
                            {
                                state.Arguments.Append(toolCall.Function.Arguments);
                            }

                            // 更新ID（如果提供了新的ID）
                            if (!string.IsNullOrEmpty(toolCall.Id))
                            {
                                state.Id = toolCall.Id;
                            }
                        }
                    }

                    // 检查是否有完整的工具调用可以输出（基于JSON完整性和流结束状态）
                    foreach (var kvp in activeFunctionCalls.ToArray())
                    {
                        var state = kvp.Value;
                        if (!string.IsNullOrEmpty(state.Name) && state.Arguments.Length > 0)
                        {
                            // 检查JSON是否完整
                            if (IsJsonComplete(state.Arguments.ToString()) || streamFinishReason.HasValue)
                            {
                                yield return CreateToolCallUpdate(openaiResponseId, modelId, state);
                                activeFunctionCalls.Remove(kvp.Key);

                                // 如果所有工具调用都已输出，清理状态
                                if (activeFunctionCalls.Count == 0)
                                {
                                    hasActiveToolCalls = false;
                                    break;
                                }
                            }
                        }
                    }
                }
                // 处理普通文本内容（只有在没有活跃工具调用时才输出）
                else if (!hasActiveToolCalls && !string.IsNullOrEmpty(choice.Delta.Content))
                {
                    ReasoningChatResponseUpdate update = new()
                    {
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created).UtcDateTime,
                        FinishReason = streamFinishReason,
                        ModelId = modelId,
                        ResponseId = openaiResponseId,
                        Role = choice.Delta.Role is not null ? new ChatRole(choice.Delta.Role) : null,
                        Thinking = false,
                        Reasoning = "",
                        Contents = new List<AIContent> { new TextContent(choice.Delta.Content) }
                    };

                    yield return update;
                }
            }

            // 处理任何剩余的未完成工具调用（安全网）
            if (activeFunctionCalls.Count > 0)
            {
                foreach (var state in activeFunctionCalls.Values)
                {
                    if (!string.IsNullOrEmpty(state.Name))
                    {
                        yield return CreateToolCallUpdate(openaiResponseId, _metadata.DefaultModelId, state);
                    }
                }
            }
        }

        /// <summary>
        /// 工具调用状态类
        /// </summary>
        private class FunctionCallState
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public StringBuilder Arguments { get; set; } = new();
        }

        /// <summary>
        /// 创建工具调用更新对象
        /// </summary>
        private ChatResponseUpdate CreateToolCallUpdate(string responseId, string modelId, FunctionCallState state)
        {
            var functionCall = new VllmFunctionToolCall
            {
                Name = state.Name,
                Arguments = state.Arguments.ToString()
            };

            return new ReasoningChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = ChatFinishReason.ToolCalls,
                ModelId = modelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Thinking = false,
                Reasoning = "",
                Contents = new List<AIContent> { ToFunctionCallContent(functionCall) }
            };
        }

        /// <summary>
        /// 检查JSON字符串是否完整
        /// </summary>
        private static bool IsJsonComplete(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            json = json.Trim();

            try
            {
                // 尝试解析JSON来验证完整性
                using var document = JsonDocument.Parse(json);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                // JSON不完整或格式错误
                return false;
            }
        }

        public JsonSerializerOptions ToolCallJsonSerializerOptions
        {
            get => _toolCallJsonSerializerOptions;
            set => _toolCallJsonSerializerOptions = Throw.IfNull(value);
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

            IDictionary<string, object?>? arguments = null;

            // 安全地解析 JSON 参数
            if (!string.IsNullOrWhiteSpace(function.Arguments))
            {
                try
                {
                    arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(function.Arguments);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // JSON 解析失败，使用原始字符串作为参数
                    arguments = new Dictionary<string, object?>
                    {
                        ["raw"] = function.Arguments
                    };
                }
                catch (Exception)
                {
                    // 其他解析错误，使用原始字符串作为参数
                    arguments = new Dictionary<string, object?>
                    {
                        ["raw"] = function.Arguments
                    };
                }
            }

            arguments ??= new Dictionary<string, object?>();

            return new FunctionCallContent(id, function.Name, arguments);
        }

        private static ChatMessage FromVllmMessage(VllmChatResponseMessage message)
        {
            var contents = new List<AIContent>();


            foreach (var function in message.ToolCalls ?? [])
            {
                var functionCall = new VllmFunctionToolCall
                {
                    Name = function.Function?.Name,
                    Arguments = function.Function?.Arguments
                };
                contents.Add(ToFunctionCallContent(functionCall));
            }


            if (contents.Count > 0)
                return new ChatMessage(new ChatRole(message.Role), contents);

            var raw = message.Content.Trim();

            if (!string.IsNullOrEmpty(raw))
                contents.Add(new TextContent(raw));
            return new ChatMessage(new ChatRole(message.Role), contents);
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
        private static ChatFinishReason? ToFinishReason(String? reason) =>
            reason switch
            {
                null => null,
                "length" => ChatFinishReason.Length,
                "stop" => ChatFinishReason.Stop,
                "tool_calls" => ChatFinishReason.ToolCalls,
                _ => new ChatFinishReason(reason),
            };

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
        /// 根据传入的消息和选项构造 VllmOpenAIChatRequest 请求对象
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

            // 初始化 extra_body 用于 generationConfig
            var generationConfig = new Dictionary<string, object?>();

            // 处理 GeminiChatOptions 的 ReasoningLevel
            if (options is GeminiChatOptions geminiOptions)
            {
                // 根据 ReasoningLevel 设置 thinkingConfig
                if (geminiOptions.ReasoningLevel == GeminiReasoningLevel.Low)
                {
                    generationConfig["thinkingConfig"] = new Dictionary<string, object?>
                    {
                        ["thinkingLevel"] = "low"
                    };
                }
                // Normal 级别使用默认行为（high），不需要显式设置
            }

            if (options is not null)
            {
                if (options.Temperature is float temperature)
                {
                    generationConfig["temperature"] = temperature;
                    (request.Options ??= new()).temperature = temperature;
                }

                if (options.TopP is float topP)
                {
                    generationConfig["topP"] = topP;
                    (request.Options ??= new()).top_p = topP;
                }
            }

            // 将 generationConfig 添加到 extra_body 中
            if (generationConfig.Count > 0)
            {
                (request.Options ??= new()).extra_body = new Dictionary<string, object?>
                {
                    ["generationConfig"] = generationConfig
                };
            }

            return request;
        }

        /// <summary>
        /// 将 ChatMessage 对象转换为一个或多个 VllmOpenAIChatRequestMessage 消消息
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
                                // GPT-OSS-120b 期望工具调用使用标准的 OpenAI 格式
                                // vLLM 模板会处理转换为 commentary channel 格式
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
                                                Arguments = System.Text.Json.JsonSerializer.Serialize(
                                                    fcc.Arguments,
                                                    ToolCallJsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))
                                            }
                                        }
                                    }
                                };
                                break;
                            }

                        case FunctionResultContent frc:
                            {
                                // GPT-OSS-120b 期望工具结果使用标准的 OpenAI 工具消息格式
                                // vLLM 模板会处理转换为 functions.name to=assistant commentary 格式
                                string resultStr = frc.Result is string s
                                    ? s
                                    : System.Text.Json.JsonSerializer.Serialize(frc.Result,
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
                    Parameters = System.Text.Json.JsonSerializer.Deserialize(function.JsonSchema, JsonContext.Default.VllmFunctionToolParameters)!,
                }
            };
        }

        /// <summary>
        /// 将 ChatMessage 转换为 Gemini Content 格式
        /// </summary>
        private GeminiContent ToGeminiContent(ChatMessage message)
        {
            var parts = new List<GeminiPart>();

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent textContent:
                        parts.Add(new GeminiPart { Text = textContent.Text });
                        break;
                    case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                        var base64Data = Convert.ToBase64String(dataContent.Data
#if NET
                            .Span);
#else
                            .ToArray());
#endif
                        parts.Add(new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = dataContent.MediaType ?? "image/jpeg",
                                Data = base64Data
                            }
                        });
                        break;
                    case FunctionCallContent fcc:
                        // 如果参数中包含 thoughtSignature，将其挂载到 GeminiPart 的 ThoughtSignature 字段
                        string? sig = null;
                        if (fcc.Arguments.ContainsKey("thoughtSignature"))
                        {
                            var sigObj = fcc.Arguments["thoughtSignature"];
                            sig = sigObj?.ToString();
                            fcc.Arguments.Remove("thoughtSignature"); // 从参数中移除，避免发送时污染函数调用参数
                        }
                        parts.Add(new GeminiPart
                        {
                            ThoughtSignature = sig,
                            FunctionCall = new GeminiFunctionCall
                            {
                                Name = fcc.Name,
                                Args = fcc.Arguments as Dictionary<string, object?> ?? new Dictionary<string, object?>(fcc.Arguments)
                            }
                        });
                        break;
                    case FunctionResultContent frc:
                        var responseData = frc.Result is string s
                            ? new Dictionary<string, object?> { ["result"] = s }
                            : frc.Result as Dictionary<string, object?> ?? new Dictionary<string, object?> { ["result"] = frc.Result };
                        // 从映射恢复函数名，若不存在则回退使用 callId
                        string fnName = string.Empty;
                        if ((_functionCallNameMap?.TryGetValue(frc.CallId ?? string.Empty, out var n) ?? false) && !string.IsNullOrEmpty(n))
                        {
                            fnName = n;
                        }
                        parts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Name = string.IsNullOrEmpty(fnName) ? frc.CallId : fnName,
                                Response = responseData
                            }
                        });
                        break;
                }
            }

            // Gemini 使用 "user" 和 "model" 角色
            string role = message.Role.Value switch
            {
                "user" => "user",
                "assistant" => "model",
                "system" => "user", // Gemini 不支持 system 角色，转换为 user
                "tool" => "function", // 工具响应
                _ => "user"
            };

            return new GeminiContent
            {
                Role = role,
                Parts = parts.ToArray()
            };
        }

        /// <summary>
        /// 将 ChatMessage 列表转换为 Gemini 请求
        /// </summary>
        private GeminiRequest ToGeminiRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var contents = messages.Select(ToGeminiContent).ToArray();

            var request = new GeminiRequest
            {
                Contents = contents
            };

            // 配置生成参数
            var genConfig = new GeminiGenerationConfig();

            if (options is GeminiChatOptions geminiOptions)
            {
                if (geminiOptions.ReasoningLevel == GeminiReasoningLevel.Low)
                {
                    genConfig.ThinkingConfig = new GeminiThinkingConfig
                    {
                        ThinkingLevel = "low"
                    };
                }
            }

            if (options?.Temperature is float temp)
            {
                genConfig.Temperature = temp;
            }

            if (options?.TopP is float topP)
            {
                genConfig.TopP = topP;
            }

            // 结构化输出
            if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
            {
                genConfig.ResponseMimeType = "application/json";
                if (jsonFormat.Schema != null)
                {
                    genConfig.ResponseSchema = jsonFormat.Schema;
                }
            }

            request.GenerationConfig = genConfig;

            // 工具定义
            if (options?.Tools is { Count: > 0 } tools && options.ToolMode is not NoneChatToolMode)
            {
                var functionDeclarations = new List<GeminiFunctionDeclaration>();

                foreach (var tool in tools.OfType<AIFunction>())
                {
                    functionDeclarations.Add(new GeminiFunctionDeclaration
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = System.Text.Json.JsonSerializer.Deserialize<object>(tool.JsonSchema)
                    });
                }

                request.Tools = new[]
                {
                    new GeminiTool
                    {
                        FunctionDeclarations = functionDeclarations.ToArray()
                    }
                };
            }

            return request;
        }

        /// <summary>
        /// 将 Gemini 响应转换为 ChatResponse
        /// 
        /// 注意：Gemini 3 的推理内容是加密的思维签名（thoughtSignature），
        /// 不是可读的推理文本。这些签名用于在多轮对话中保持推理上下文。
        /// </summary>
        private ChatResponse FromGeminiResponse(GeminiResponse geminiResponse, ChatOptions? options)
        {
            if (geminiResponse.Candidates == null || geminiResponse.Candidates.Length == 0)
            {
                throw new InvalidOperationException("Gemini API 未返回任何候选响应。");
            }

            var candidate = geminiResponse.Candidates[0];
            var content = candidate.Content;

            if (content == null || content.Parts.Length == 0)
            {
                throw new InvalidOperationException("Gemini API 返回的内容为空。");
            }

            var aiContents = new List<AIContent>();
            var thoughtSignatures = new List<string>();
            string? lastSig = null; // 追踪最近签名供未包含签名的函数调用使用
            foreach (var part in content.Parts)
            {
                if (!string.IsNullOrEmpty(part.ThoughtSignature))
                {
                    thoughtSignatures.Add(part.ThoughtSignature);
                    lastSig = part.ThoughtSignature;
                }

                if (!string.IsNullOrEmpty(part.Text))
                {
                    aiContents.Add(new TextContent(part.Text));
                }

                if (part.FunctionCall != null)
                {
#if NET
                    var callId = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                    var callId = Guid.NewGuid().ToString().Substring(0, 8);
#endif
                    var args = part.FunctionCall.Args ?? new Dictionary<string, object?>();
                    // 如果当前函数调用没有签名但之前存在签名则补充
                    if (string.IsNullOrEmpty(part.ThoughtSignature) && !string.IsNullOrEmpty(lastSig))
                    {
                        args["thoughtSignature"] = lastSig;
                    }
                    else if (!string.IsNullOrEmpty(part.ThoughtSignature))
                    {
                        args["thoughtSignature"] = part.ThoughtSignature;
                    }
                    aiContents.Add(new FunctionCallContent(
                        callId,
                        part.FunctionCall.Name,
                        args
                    ));
                    (_functionCallNameMap ??= new()).TryAdd(callId, part.FunctionCall.Name ?? string.Empty);
                }
            }

            // Gemini 3 的推理是内部的，通过加密签名维护上下文
            // 实际的推理过程不会作为文本暴露
            string reasoningNote = thoughtSignatures.Count > 0
                ? $"[Gemini 3 内部推理 - {thoughtSignatures.Count} 个思维签名]"
                : "";

            var message = new ChatMessage(new ChatRole("model"), aiContents);

            var finishReason = candidate.FinishReason switch
            {
                "STOP" => ChatFinishReason.Stop,
                "MAX_TOKENS" => ChatFinishReason.Length,
                "SAFETY" => new ChatFinishReason("safety"),
                "RECITATION" => new ChatFinishReason("recitation"),
                _ => (ChatFinishReason?)null
            };

            UsageDetails? usage = null;
            if (geminiResponse.UsageMetadata != null)
            {
                usage = new UsageDetails
                {
                    InputTokenCount = geminiResponse.UsageMetadata.PromptTokenCount,
                    OutputTokenCount = geminiResponse.UsageMetadata.CandidatesTokenCount,
                    TotalTokenCount = geminiResponse.UsageMetadata.TotalTokenCount
                };
            }

            return new ReasoningChatResponse(message, reasoningNote)
            {
                CreatedAt = DateTimeOffset.UtcNow,
                FinishReason = finishReason,
                ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = Guid.NewGuid().ToString("N"),
                Usage = usage
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

        /// <summary>
        /// 分析推理结构，提取CoT信息，参考Python实现
        /// </summary>
        private static (bool hasReasoning, string reasoningText, string reasoningType) AnalyzeReasoningStructure(object? reasoningContent)
        {
            if (reasoningContent == null)
                return (false, "", "unknown");

            bool hasReasoning = false;
            string reasoningText = "";
            string reasoningType = "unknown";

            try
            {
                // 如果是字符串，直接使用
                if (reasoningContent is string stringContent && !string.IsNullOrEmpty(stringContent))
                {
                    hasReasoning = true;
                    reasoningText = stringContent;
                    reasoningType = "standard";
                }
                // 如果是JsonElement，进行解析
                else if (reasoningContent is JsonElement jsonElement)
                {
                    // 检查基本reasoning字段
                    if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("reasoning", out var reasoningProp))
                    {
                        if (reasoningProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(reasoningProp.GetString()))
                        {
                            hasReasoning = true;
                            reasoningText = reasoningProp.GetString()!;
                            reasoningType = "standard";
                        }
                    }

                    // 检查详细reasoning信息
                    if (jsonElement.TryGetProperty("reasoning_details", out var reasoningDetailsProp) &&
                        reasoningDetailsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in reasoningDetailsProp.EnumerateArray())
                        {
                            if (detail.TryGetProperty("type", out var typeProp) &&
                                typeProp.ValueKind == JsonValueKind.String &&
                                typeProp.GetString() == "reasoning.text")
                            {
                                reasoningType = "structured";

                                // 如果主reasoning为空，使用详细信息
                                if (string.IsNullOrEmpty(reasoningText) &&
                                    detail.TryGetProperty("text", out var textProp) &&
                                    textProp.ValueKind == JsonValueKind.String)
                                {
                                    hasReasoning = true;
                                    reasoningText = textProp.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
                // 尝试将对象序列化为JSON字符串然后解析
                else if (reasoningContent != null)
                {
                    var jsonString = System.Text.Json.JsonSerializer.Serialize(reasoningContent);

                    if (!string.IsNullOrEmpty(jsonString) && jsonString != "null" && jsonString != "{}")
                    {
                        hasReasoning = true;
                        reasoningText = jsonString;
                        reasoningType = "json";

                        // 尝试解析结构化内容
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(jsonString);
                            var analysisResult = AnalyzeReasoningStructure(jsonDoc.RootElement);
                            if (analysisResult.hasReasoning)
                            {
                                return analysisResult;
                            }
                        }
                        catch
                        {
                            // 如果解析失败，继续使用原始JSON字符串
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 如果解析失败，尝试将内容转换为字符串
                if (reasoningContent != null)
                {
                    hasReasoning = true;
                    reasoningText = reasoningContent.ToString() ?? "";
                    reasoningType = "fallback";
                }
            }

            return (hasReasoning, reasoningText, reasoningType);
        }
    }
}
