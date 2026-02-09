using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    public abstract class VllmBaseChatClient : IChatClient
    {
        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;
        private readonly ChatClientMetadata _metadata;
        private readonly string _apiChatEndpoint;
        private readonly HttpClient _httpClient;
        private JsonSerializerOptions _toolCallJsonSerializerOptions = AIJsonUtilities.DefaultOptions;

        protected VllmBaseChatClient(string endpoint, string? token, string? modelId, HttpClient? httpClient)
        {
            _ = Throw.IfNull(endpoint);
            if (modelId is not null)
            {
                _ = Throw.IfNullOrWhitespace(modelId);
            }

            _apiChatEndpoint = endpoint ?? "http://localhost:8000/{0}/{1}";
            _httpClient = httpClient ?? VllmUtilities.SharedClient;
            if (!string.IsNullOrWhiteSpace(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }

            _metadata = new("vllm", new Uri(_apiChatEndpoint), modelId);
        }

        protected ChatClientMetadata Metadata => _metadata;

        protected string ApiChatEndpoint => _apiChatEndpoint;

        protected HttpClient HttpClient => _httpClient;

        public JsonSerializerOptions ToolCallJsonSerializerOptions
        {
            get => _toolCallJsonSerializerOptions;
            set => _toolCallJsonSerializerOptions = Throw.IfNull(value);
        }

        public virtual void Dispose()
        {
            if (_httpClient != VllmUtilities.SharedClient)
            {
                _httpClient.Dispose();
            }
        }

        public virtual async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(messages);

            string apiEndpoint = GetChatEndpoint();
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
            string reason = responseMessage?.ReasoningContent?.ToString() ?? string.Empty;
            var retMessage = FromVllmMessage(responseMessage!, options);
            bool hasToolCall = retMessage.Contents.Any(c => c is FunctionCallContent);

            return new ReasoningChatResponse(retMessage, reason)
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = hasToolCall ? ChatFinishReason.ToolCalls : ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseVllmChatResponseUsage(response),
            };
        }

        public virtual object? GetService(Type serviceType, object? serviceKey = null)
        {
            return
                serviceKey is not null ? null :
                serviceType == typeof(ChatClientMetadata) ? _metadata :
                serviceType.IsInstanceOfType(this) ? this :
                null;
        }

        public virtual async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(messages);
            string apiEndpoint = GetChatEndpoint();
            using HttpRequestMessage request = new(HttpMethod.Post, apiEndpoint)
            {
                Content = JsonContent.Create(ToVllmChatRequest(messages, options, stream: true), JsonContext.Default.VllmOpenAIChatRequest)
            };
            using var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

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
            string bufferMsg = string.Empty;
            string bufferName = string.Empty;
            string bufferParams = string.Empty;
            bool thinking = true;
            string bufferToolCall = string.Empty;
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

                if (chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.Length == 1)
                {
                    if (string.IsNullOrEmpty(bufferName))
                    {
                        bufferName = chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.FirstOrDefault()?.Function?.Name ?? "";
                    }
                    bufferParams += chunk.Choices.FirstOrDefault()?.Delta?.ToolCalls?.FirstOrDefault()?.Function?.Arguments?.ToString() ?? "";
                }

                if (chunk.Choices.FirstOrDefault()?.Delta is { } message)
                {
                    bufferMsg += message.Content ?? string.Empty;
                    var bufferCopy = bufferMsg;
                    var funcList = new List<VllmFunctionToolCall>();

                    if (message.ReasoningContent != null)
                    {
                        yield return BuildTextUpdate(responseId, message.ReasoningContent, true);
                        continue;
                    }

                    thinking = false;

                    foreach (var call in message.ToolCalls ?? [])
                    {
                        bool isJsonComplete = false;
                        try
                        {
                            if (!string.IsNullOrEmpty(bufferName) && !string.IsNullOrEmpty(bufferParams))
                            {
                                _ = JsonConvert.DeserializeObject(bufferParams);
                                isJsonComplete = ToolcallParser.GetBraceDepth(bufferParams) == 0;
                            }
                        }
                        catch (Exception)
                        {
                        }

                        if (isJsonComplete)
                        {
                            funcList.Add(new VllmFunctionToolCall
                            {
                                Name = bufferName,
                                Arguments = bufferParams
                            });
                            bufferParams = string.Empty;
                            bufferName = string.Empty;
                        }
                    }

                    ToolcallParser.TryFlushClosedToolCallBlocks(ref bufferCopy, out var tcalls);
                    funcList.AddRange(tcalls);

                    if (funcList.Count > 0)
                    {
                        foreach (var call in funcList)
                        {
                            yield return BuildToolCallUpdate(responseId, call);
                        }

                        bufferCopy = string.Empty;
                        continue;
                    }

                    bool inToolCallBlock = ToolcallParser.IsInsideIncompleteToolCall(bufferCopy);
                    bool isUnclosed = ToolcallParser.HasUnclosedToolCall(bufferMsg);

                    if (!inToolCallBlock && !isUnclosed &&
                        funcList.Count == 0 &&
                        !string.IsNullOrEmpty(message.Content))
                    {
                        if (message.Content is "<" or "<tool")
                        {
                            bufferToolCall += message.Content;
                        }
                        if (bufferToolCall.Length > 0 && bufferToolCall.Length < 11)
                        {
                            bufferToolCall += message.Content;
                            continue;
                        }
                        yield return BuildTextUpdate(responseId, message.Content, thinking);
                    }
                }
            }
        }

        protected virtual string GetChatEndpoint() => string.Format(_apiChatEndpoint, "v1", "chat/completions");

        protected virtual ChatResponseUpdate BuildTextUpdate(string responseId, string text, bool thinking)
        {
            return new ReasoningChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                ModelId = _metadata.DefaultModelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Thinking = thinking,
                Contents = new List<AIContent> { new TextContent(text) }
            };
        }

        private protected virtual ChatResponseUpdate BuildToolCallUpdate(string responseId, VllmFunctionToolCall call)
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

        private static UsageDetails? ParseVllmChatResponseUsage(VllmChatResponse response)
        {
            if (response?.Usage == null)
            {
                return null;
            }

            return new UsageDetails
            {
                InputTokenCount = response.Usage.PromptTokens,
                OutputTokenCount = response.Usage.CompletionTokens,
                TotalTokenCount = response.Usage.TotalTokens
            };
        }

        protected static ChatFinishReason? ToFinishReason(string reason) =>
            reason switch
            {
                null => null,
                "length" => ChatFinishReason.Length,
                "stop" => ChatFinishReason.Stop,
                _ => new ChatFinishReason(reason),
            };

        private protected virtual ChatMessage FromVllmMessage(VllmChatResponseMessage message, ChatOptions? options)
        {
            var contents = new List<AIContent>();

            var hasToolCalls = message.ToolCalls?.Length > 0;
            var allowToolCallParsing = options?.ToolMode is not NoneChatToolMode && options?.Tools is { Count: > 0 };
            foreach (var toolcall in message.ToolCalls ?? [])
            {
                contents.Add(ToFunctionCallContent(new VllmFunctionToolCall
                {
                    Name = toolcall.Function?.Name ?? "",
                    Arguments = toolcall.Function?.Arguments?.ToString() ?? "{}"
                }));
            }

            if (message.Content != null)
            {
                var raw = message.Content;
                var afterToolCalls = raw;
                if (!hasToolCalls && allowToolCallParsing)
                {
                    var tcList = ToolcallParser.ParseToolCalls(raw, out afterToolCalls);

                    foreach (var call in tcList)
                    {
                        contents.Add(ToFunctionCallContent(call));
                    }

                    var (jsonPieces, rest) = ToolcallParser.SliceJsonFragments(afterToolCalls);
                    afterToolCalls = rest;

                    foreach (var json in jsonPieces)
                    {
                        var call = ToolcallParser.TryParseToolCallJson(json);
                        if (call != null)
                        {
                            contents.Add(ToFunctionCallContent(call));
                        }
                    }
                }

                var restText = contents.Count == 0 ? raw : afterToolCalls;

                restText = restText.Trim();
                var thinkIndex = restText.IndexOf("</think>", StringComparison.Ordinal);
                if (thinkIndex >= 0)
                {
                    restText = restText[(thinkIndex + 8)..].Trim();
                }

                if (!string.IsNullOrEmpty(restText))
                {
                    var cleanedText = allowToolCallParsing || hasToolCalls
                        ? ToolcallParser.StripToolCallResidues(restText)
                        : restText;
                    contents.Add(new TextContent(cleanedText));
                }
            }
            return new ChatMessage(new ChatRole(message.Role), contents);
        }

        private protected static FunctionCallContent ToFunctionCallContent(VllmFunctionToolCall function)
        {
#if NET
            var id = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
            var id = Guid.NewGuid().ToString().Substring(0, 8);
#endif
            var arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(function.Arguments);
            return new FunctionCallContent(id, function.Name, arguments);
        }

        protected static JsonElement? ToVllmChatResponseFormat(ChatResponseFormat? format)
        {
            if (format is ChatResponseFormatJson jsonFormat)
            {
                return jsonFormat.Schema ?? _schemalessJsonResponseFormatValue;
            }

            return null;
        }

        private protected virtual VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            ValidateMessages(messages, options);

            VllmOpenAIChatRequest request = new()
            {
                Format = ToVllmChatResponseFormat(options?.ResponseFormat),
                Messages = messages.SelectMany(ToVllmChatRequestMessages).ToArray(),
                Model = options?.ModelId ?? _metadata.DefaultModelId ?? string.Empty,
                Stream = stream,
                Tools = GetTools(options),
            };

            ApplyRequestOptions(request, options);
            return request;
        }

        private protected virtual IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
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

        private protected virtual IEnumerable<VllmTool>? GetTools(ChatOptions? options)
        {
            return options?.ToolMode is not NoneChatToolMode && options?.Tools is { Count: > 0 } tools
                ? tools.OfType<AIFunction>().Select(ToVllmTool)
                : null;
        }

        private protected virtual void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            if (options is null)
            {
                return;
            }

            if (options.Temperature is float temperature)
            {
                (request.Options ??= new()).temperature = temperature;
            }

            if (options.TopP is float topP)
            {
                (request.Options ??= new()).top_p = topP;
            }
        }

        protected virtual void ValidateMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
        }

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
