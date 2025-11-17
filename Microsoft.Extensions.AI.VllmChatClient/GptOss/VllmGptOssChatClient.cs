using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.AI.VllmChatClient.GptOss
{
    public class VllmGptOssChatClient : IChatClient
    {
        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;

        /// <summary>关于客户端的元数据</summary>
        private readonly ChatClientMetadata _metadata;

        /// <summary>api/chat 端点 URI</summary>
        private readonly string _apiChatEndpoint;

        /// <summary>用于发送请求的 HttpClient 对象</summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Provides the default <see cref="JsonSerializerOptions"/> used for tool call serialization.
        /// </summary>
        /// <remarks>This field is initialized with the default options from <see
        /// cref="AIJsonUtilities.DefaultOptions"/>. It is intended for internal use when serializing or deserializing
        /// tool call payloads.</remarks>
        private JsonSerializerOptions _toolCallJsonSerializerOptions = AIJsonUtilities.DefaultOptions;
        public VllmGptOssChatClient(string endpoint, string? token = null, string? modelId = "gpt-oss-120b", HttpClient? httpClient = null)
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
            
            // 检查 endpoint 是否已经包含 chat/completions 路径
            if (endpoint.Contains("/chat/completions"))
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

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            //_apiToken = token ?? "0";

            _metadata = new("vllm", new Uri(_apiChatEndpoint.Contains("{0}") ? string.Format(_apiChatEndpoint, "v1", "chat/completions") : _apiChatEndpoint), modelId);
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

            // 使用 SetUpChatOptions 方法处理消息和选项
            messages = SetUpChatOptions(messages, options);

            // 如果 _apiChatEndpoint 包含占位符，使用格式化；否则直接使用
            string apiEndpoint = _apiChatEndpoint.Contains("{0}") 
                ? string.Format(_apiChatEndpoint, "v1", "chat/completions")
                : _apiChatEndpoint;

            using var httpResponse = await _httpClient.PostAsJsonAsync(
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

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return
                serviceKey is not null ? null :
                serviceType == typeof(ChatClientMetadata) ? _metadata :
                serviceType.IsInstanceOfType(this) ? this :
                null;
        }


        /// <summary>
        /// 检查每一条消息，如果该条消息是工具调用的信息，则将Text 内容设置为空.
        /// </summary>
        /// <param name="messages"></param>
        /// <returns></returns>
        private IEnumerable<ChatMessage> ClearMessages(IEnumerable<ChatMessage> messages)
        {
            foreach (var msg in messages)
            {

            }
            return messages;
        }

        /// <summary>
        /// 默认系统提示词
        /// </summary>
        private static readonly string DefaultSystemPrompt = @"你必须遵循以下硬性规则：
1) 当且仅当需要调用工具解决用户问题时，当前轮消息必须只返回工具调用（tool_calls），并将 content 置空或不返回任何自然语言。
2) 如果不需要调用工具，当前轮消息不得返回任何工具调用，仅以自然语言回答。
3) 不得在包含 tool_calls 的同一条消息中加入解释、道歉、前后缀、思考过程或任何自然语言。
4) 若需要多个工具，请按需要返回多个 tool_calls；除 tool_calls 外不得返回其它字段内容（content 为空）。
5) 若无法确定是否需要工具，先不调用工具，直接向用户询问澄清（此时只能自然语言，无 tool_calls）。";

        /// <summary>
        /// 检查系统消息是否已包含默认提示词的关键标识
        /// </summary>
        private static readonly string DefaultPromptMarker = "你必须遵循以下硬性规则：";

        /// <summary>
        /// 设置聊天选项并处理 system prompt
        /// </summary>
        /// <param name="messages">原始消息列表</param>
        /// <param name="options">聊天选项</param>
        /// <returns>处理后的消息列表</returns>
        private IEnumerable<ChatMessage> SetUpChatOptions(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            // 验证 system messages 的数量
            var messagesList = messages.ToList();
            var systemMessages = messagesList.Where(m => m.Role == ChatRole.System).ToList();
            if (systemMessages.Count > 1)
            {
                throw new ArgumentException("Messages 中只能包含一条 system message。", nameof(messages));
            }

            // 检查是否已有system message
            var systemMessageIndex = messagesList.FindIndex(m => m.Role == ChatRole.System);
            bool hasExistingSystemMessage = systemMessageIndex >= 0;
            bool alreadyHasDefaultPrompt = false;

            // 如果已有系统消息，检查是否已包含默认提示词
            if (hasExistingSystemMessage)
            {
                var existingSystemMessage = messagesList[systemMessageIndex];
                var existingContent = string.Empty;
                
                foreach (var content in existingSystemMessage.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        existingContent += textContent.Text + "\n";
                    }
                }
                
                // 检查是否已包含默认提示词标识
                alreadyHasDefaultPrompt = existingContent.Contains(DefaultPromptMarker);
                
                // 如果还没有默认提示词，则添加
                if (!alreadyHasDefaultPrompt)
                {
                    var defaultSystemMessage = DefaultSystemPrompt;
                    
                    // 如果是 GptOssChatOptions，添加推理级别信息
                    if (options is GptOssChatOptions gptOssOptions)
                    {
                        var reasoningLevel = gptOssOptions.ReasoningLevel switch
                        {
                            GptOssReasoningLevel.Low => "low",
                            GptOssReasoningLevel.Medium => "medium", 
                            GptOssReasoningLevel.High => "high",
                            _ => "medium"
                        };

                        defaultSystemMessage += $"\nReasoning: {reasoningLevel}";
                    }
                    
                    // 构建完整的系统消息：默认系统消息 + 用户的系统消息
                    var combinedSystemMessage = defaultSystemMessage;
                    if (!string.IsNullOrWhiteSpace(existingContent.Trim()))
                    {
                        combinedSystemMessage += $"\n\n# Additional Instructions\n{existingContent.Trim()}";
                    }
                    
                    // 替换原有的system message
                    var newSystemMessage = new ChatMessage(ChatRole.System, new List<AIContent> { new TextContent(combinedSystemMessage) });
                    messagesList[systemMessageIndex] = newSystemMessage;
                }
            }
            else
            {
                // 如果没有system message，在开头添加默认系统消息
                var defaultSystemMessage = DefaultSystemPrompt;
                
                // 如果是 GptOssChatOptions，添加推理级别信息
                if (options is GptOssChatOptions gptOssOptions)
                {
                    var reasoningLevel = gptOssOptions.ReasoningLevel switch
                    {
                        GptOssReasoningLevel.Low => "low",
                        GptOssReasoningLevel.Medium => "medium", 
                        GptOssReasoningLevel.High => "high",
                        _ => "medium"
                    };

                    defaultSystemMessage += $"\nReasoning: {reasoningLevel}";
                }
                
                var newSystemMessage = new ChatMessage(ChatRole.System, new List<AIContent> { new TextContent(defaultSystemMessage) });
                messagesList.Insert(0, newSystemMessage);
            }

            // 如果是 GptOssChatOptions 并且有工具，设置工具模式
            if (options is GptOssChatOptions && options.Tools?.Count > 0)
            {
                options.ToolMode = ChatToolMode.Auto;
            }

            return messagesList;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // 检查 messages 参数是否为 null
            _ = Throw.IfNull(messages);

            // 使用 SetUpChatOptions 方法处理消息和选项
            messages = SetUpChatOptions(messages, options);

            // 如果 _apiChatEndpoint 包含占位符，使用格式化；否则直接使用
            string apiEndpoint = _apiChatEndpoint.Contains("{0}") 
                ? string.Format(_apiChatEndpoint, "v1", "chat/completions")
                : _apiChatEndpoint;

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
                            ResponseId = responseId,
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
                                yield return CreateToolCallUpdate(responseId, modelId, state);
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
                                yield return CreateToolCallUpdate(responseId, modelId, state);
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
                        ResponseId = responseId,
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
                        yield return CreateToolCallUpdate(responseId, _metadata.DefaultModelId, state);
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

            
            if (contents.Count>0)
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
        private static ChatFinishReason? ToFinishReason(string? reason) =>
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

            // 尝试启用推理功能的参数 - 基于 GPT-OSS 文档
            (request.Options ??= new()).extra_body = new Dictionary<string, object?>
            {
                ["reasoning"] = true,
                ["include_reasoning"] = true,
                ["enable_reasoning"] = true,
                ["stream_reasoning"] = true
            };

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
