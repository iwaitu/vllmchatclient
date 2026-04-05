using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    public abstract class VllmBaseChatClient : IChatClient
    {
        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;
        private static readonly ConcurrentDictionary<string, (SkillCatalog? catalog, DateTime timestamp)> _skillCatalogCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ChatClientMetadata _metadata;
        private readonly string _apiChatEndpoint;
        private readonly HttpClient _httpClient;
        private JsonSerializerOptions _toolCallJsonSerializerOptions = AIJsonUtilities.DefaultOptions;

        private sealed record SkillCatalog(string Instruction, SkillManifest[] Skills);

        private sealed record SkillManifest(string Name, string Description, string FileName, string RelativePath, string FullPath);

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

        protected virtual string ProviderName => "vllm";

        internal virtual ChatResponseUpdate? HandleStreamingReasoningContent(Delta delta, string responseId, string modelId)
        {
            if (delta.ReasoningContent != null)
            {
                return BuildTextUpdate(responseId, delta.ReasoningContent, true);
            }

            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                return BuildTextUpdate(responseId, delta.Reasoning, true);
            }

            if (delta.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail && !string.IsNullOrEmpty(detail.Text))
            {
                // Streaming delta for reasoning details might come piece by piece in Text property
                return BuildTextUpdate(responseId, detail.Text, true);
            }

            return null;
        }

        /// <summary>
        /// Whether to enable legacy text-based tool-call parsing fallback, e.g. &lt;tool_call&gt;...&lt;/tool_call&gt;.
        /// Default is false to prefer standard OpenAI-compatible tool_calls fields.
        /// </summary>
        protected virtual bool EnableLegacyToolCallTextFallback(ChatOptions? options)
            => options is VllmChatOptions vllmOptions && vllmOptions.EnableLegacyToolCallTextFallback;

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
            const int maxAttempts = 2; // retry at most once for malformed tool-call protocol payloads

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
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

                if (response.Choices is null || response.Choices.Length == 0)
                {
                    throw new InvalidOperationException("未返回任何响应选项。");
                }

                var choice = response.Choices[0];
                var responseMessage = choice.Message;
                string reason = responseMessage?.ReasoningContent?.ToString() ?? string.Empty;

                if (string.IsNullOrEmpty(reason))
                {
                    reason = responseMessage?.Reasoning ?? string.Empty;
                }

                if (string.IsNullOrEmpty(reason) && responseMessage?.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail)
                {
                    reason = detail.Text;
                }

                try
                {
                    var retMessage = FromVllmMessage(responseMessage!, options);
                    bool hasToolCall = retMessage.Contents.Any(c => c is FunctionCallContent);

                    return new ReasoningChatResponse(retMessage, reason)
                    {
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                        FinishReason = hasToolCall ? ChatFinishReason.ToolCalls : ToFinishReason(response.Choices?.FirstOrDefault()?.FinishReason),
                        ModelId = response.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                        ResponseId = response.Id,
                        Usage = ParseVllmChatResponseUsage(response),
                    };
                }
                catch (InvalidOperationException ex) when (
                    attempt < maxAttempts &&
                    string.Equals(choice.FinishReason, "tool_calls", StringComparison.OrdinalIgnoreCase) &&
                    IsMalformedToolCallProtocolException(ex))
                {
                    // Retry once for provider protocol inconsistency:
                    // finish_reason == tool_calls but tool_calls payload is malformed (e.g., empty function.name)
                    int backoffMs = 100 + Random.Shared.Next(0, 201);
                    Trace.TraceWarning(
                        "[{0}] Malformed tool-call protocol payload in non-stream GetResponseAsync. Retry attempt {1}/{2} after {3}ms (finish_reason=tool_calls, responseId={4}, model={5}). Error={6}",
                        ProviderName,
                        attempt + 1,
                        maxAttempts,
                        backoffMs,
                        response.Id ?? "(null)",
                        response.Model ?? options?.ModelId ?? _metadata.DefaultModelId ?? "(null)",
                        ex.Message);

                    await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            throw new InvalidOperationException("Unreachable: GetResponseAsync retry loop exhausted unexpectedly.");
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
            bool enableLegacyToolCallTextFallback = EnableLegacyToolCallTextFallback(options);
            string bufferMsg = string.Empty;
            // 按 tool_calls[].index 缓冲多个并行工具调用
            var toolCallBuffers = new Dictionary<int, (string? Id, string Name, string Arguments)>();
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
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                {
                    continue;
                }

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

                if (chunk?.Usage is { } streamUsage)
                {
                    yield return new UsageChatResponseUpdate
                    {
                        CreatedAt = chunk.Created > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(chunk.Created)
                            : DateTimeOffset.Now,
                        ModelId = chunk.Model ?? options?.ModelId ?? _metadata.DefaultModelId,
                        ResponseId = responseId,
                        Usage = ParseVllmUsage(streamUsage),
                    };
                }

                if (chunk == null || chunk.Choices == null || chunk.Choices.Count == 0)
                {
                    continue;
                }

                var choice = chunk.Choices[0];

                // 按 index 缓冲每个工具调用的 id/name/arguments 片段
                if (choice.Delta?.ToolCalls is { Length: > 0 } deltaToolCalls)
                {
                    foreach (var tc in deltaToolCalls)
                    {
                        int idx = tc.Index ?? 0;
                        if (!toolCallBuffers.TryGetValue(idx, out var buf))
                        {
                            buf = (null, string.Empty, string.Empty);
                        }

                        if (!string.IsNullOrEmpty(tc.Id))
                        {
                            buf.Id = tc.Id;
                        }

                        if (!string.IsNullOrEmpty(tc.Function?.Name))
                        {
                            buf.Name = tc.Function.Name;
                        }

                        buf.Arguments += tc.Function?.Arguments?.ToString() ?? string.Empty;
                        toolCallBuffers[idx] = buf;
                    }
                }

                // 当 finish_reason 为 tool_calls 时，flush 所有缓冲的工具调用
                if (choice.FinishReason == "tool_calls" && toolCallBuffers.Count > 0)
                {
                    foreach (var kvp in toolCallBuffers.OrderBy(k => k.Key))
                    {
                        if (!string.IsNullOrEmpty(kvp.Value.Name))
                        {
                            yield return BuildToolCallUpdate(responseId, new VllmFunctionToolCall
                            {
                                Name = kvp.Value.Name,
                                Arguments = kvp.Value.Arguments
                            }, kvp.Value.Id);
                        }
                    }

                    toolCallBuffers.Clear();
                }

                if (choice.Delta is { } message)
                {
                    var reasoningUpdate = HandleStreamingReasoningContent(message, responseId, chunk.Model ?? Metadata.DefaultModelId ?? string.Empty);
                    if (reasoningUpdate != null)
                    {
                        // reasoning 可能与 tool_calls/content 同帧共存，不能直接 continue
                        yield return reasoningUpdate;
                    }

                    bufferMsg += message.Content ?? string.Empty;
                    var bufferCopy = bufferMsg;
                    var funcList = new List<(VllmFunctionToolCall Call, string? CallId)>();

                    // 逐 chunk 检测已完成的工具调用（单工具场景兼容）
                    foreach (var call in message.ToolCalls ?? [])
                    {
                        int idx = call.Index ?? 0;
                        if (toolCallBuffers.TryGetValue(idx, out var buf))
                        {
                            bool isJsonComplete = false;
                            try
                            {
                                if (!string.IsNullOrEmpty(buf.Name) && !string.IsNullOrEmpty(buf.Arguments))
                                {
                                    _ = JsonConvert.DeserializeObject(buf.Arguments);
                                    isJsonComplete = ToolcallParser.GetBraceDepth(buf.Arguments) == 0;
                                }
                            }
                            catch (Exception)
                            {
                            }

                            if (isJsonComplete)
                            {
                                funcList.Add((new VllmFunctionToolCall
                                {
                                    Name = buf.Name,
                                    Arguments = buf.Arguments
                                }, buf.Id));
                                toolCallBuffers.Remove(idx);
                            }
                        }
                    }

                    if (enableLegacyToolCallTextFallback)
                    {
                        ToolcallParser.TryFlushClosedToolCallBlocks(ref bufferCopy, out var tcalls);
                        funcList.AddRange(tcalls.Select(call => (call, (string?)null)));
                    }

                    if (funcList.Count > 0)
                    {
                        foreach (var call in funcList)
                        {
                            yield return BuildToolCallUpdate(responseId, call.Call, call.CallId);
                        }

                        bufferCopy = string.Empty;
                        continue;
                    }

                    bool inToolCallBlock = enableLegacyToolCallTextFallback && ToolcallParser.IsInsideIncompleteToolCall(bufferCopy);
                    bool isUnclosed = enableLegacyToolCallTextFallback && ToolcallParser.HasUnclosedToolCall(bufferMsg);

                    if (!inToolCallBlock && !isUnclosed &&
                        !string.IsNullOrEmpty(message.Content))
                    {
                        if (enableLegacyToolCallTextFallback && (message.Content is "<" or "<tool"))
                        {
                            bufferToolCall += message.Content;
                        }
                        if (enableLegacyToolCallTextFallback && bufferToolCall.Length > 0 && bufferToolCall.Length < 11)
                        {
                            bufferToolCall += message.Content;
                            continue;
                        }
                        message.Content = message.Content.Replace("</think>", "");
                        thinking = false;
                        yield return BuildTextUpdate(responseId, message.Content, thinking);
                    }
                }
            }

            // 流结束后，flush 所有仍在缓冲中的工具调用（防止 finish_reason 缺失或非标准值）
            if (toolCallBuffers.Count > 0)
            {
                foreach (var kvp in toolCallBuffers.OrderBy(k => k.Key))
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Name))
                    {
                        yield return BuildToolCallUpdate(responseId, new VllmFunctionToolCall
                        {
                            Name = kvp.Value.Name,
                            Arguments = kvp.Value.Arguments
                        }, kvp.Value.Id);
                    }
                }

                toolCallBuffers.Clear();
            }

            // 流结束后，flush bufferMsg 中可能残留的 <tool_call> 块
            if (enableLegacyToolCallTextFallback && !string.IsNullOrEmpty(bufferMsg))
            {
                ToolcallParser.TryFlushClosedToolCallBlocks(ref bufferMsg, out var remainingCalls);
                foreach (var call in remainingCalls)
                {
                    yield return BuildToolCallUpdate(responseId, call, null);
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

        private protected virtual ChatResponseUpdate BuildToolCallUpdate(string responseId, VllmFunctionToolCall call, string? callId = null)
        {
            return new ChatResponseUpdate
            {
                CreatedAt = DateTimeOffset.Now,
                FinishReason = ChatFinishReason.ToolCalls,
                ModelId = _metadata.DefaultModelId,
                ResponseId = responseId,
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { ToFunctionCallContent(call, callId) }
            };
        }

        private static UsageDetails? ParseVllmChatResponseUsage(VllmChatResponse response)
        {
            return ParseVllmUsage(response?.Usage);
        }

        private static UsageDetails? ParseVllmUsage(Usage? usage)
        {
            if (usage == null)
            {
                return null;
            }

            return new UsageDetails
            {
                InputTokenCount = usage.PromptTokens,
                OutputTokenCount = usage.CompletionTokens,
                TotalTokenCount = usage.TotalTokens
            };
        }

        protected static ChatFinishReason? ToFinishReason(string? reason) =>
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
            var allowLegacyToolCallTextFallback = EnableLegacyToolCallTextFallback(options);
            var allowToolCallParsing = allowLegacyToolCallTextFallback &&
                                       options?.ToolMode is not NoneChatToolMode &&
                                       options?.Tools is { Count: > 0 };
            if (message.ToolCalls is { Length: > 0 } messageToolCalls)
            {
                var invalidToolCalls = messageToolCalls
                    .Where(tc => string.IsNullOrWhiteSpace(tc.Function?.Name))
                    .Select(tc => new
                    {
                        tc.Id,
                        ArgumentsLength = tc.Function?.Arguments?.ToString()?.Length ?? 0
                    })
                    .ToList();

                if (invalidToolCalls.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Provider returned {invalidToolCalls.Count} tool_calls with empty function.name in non-stream response. " +
                        $"Ids=[{string.Join(", ", invalidToolCalls.Select(x => x.Id ?? ""))}]");
                }

                foreach (var toolcall in messageToolCalls)
                {
                    contents.Add(ToFunctionCallContent(new VllmFunctionToolCall
                    {
                        Name = toolcall.Function?.Name ?? "",
                        Arguments = toolcall.Function?.Arguments?.ToString() ?? "{}"
                    }, toolcall.Id));
                }
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
                    var shouldStripToolCallResidues = hasToolCalls
                        || contents.Any(c => c is FunctionCallContent)
                        || restText.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase)
                        || restText.Contains("</tool_call>", StringComparison.OrdinalIgnoreCase);

                    var cleanedText = shouldStripToolCallResidues
                        ? ToolcallParser.StripToolCallResidues(restText)
                        : restText;
                    contents.Add(new TextContent(cleanedText));
                }
            }
            return new ChatMessage(new ChatRole(message.Role ?? "assistant"), contents);
        }

        private protected static FunctionCallContent ToFunctionCallContent(VllmFunctionToolCall function, string? callId = null)
        {
            if (string.IsNullOrWhiteSpace(function?.Name))
            {
                var argsLength = function?.Arguments?.Length ?? 0;
                throw new InvalidOperationException(
                    $"Cannot create FunctionCallContent: function name is empty. callId={callId ?? "(null)"}, argumentsLength={argsLength}.");
            }

            string id = callId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
#if NET
                id = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                id = Guid.NewGuid().ToString().Substring(0, 8);
#endif
            }
            var rawArguments = function.Arguments ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawArguments))
            {
                rawArguments = "{}";
                Trace.TraceWarning(
                    "[{0}] Tool call arguments is empty/whitespace; normalizing to {{}} for function '{1}' (callId={2}).",
                    "vllm",
                    function.Name,
                    id);
            }

            IDictionary<string, object?>? arguments;
            if (TryParseFunctionArgumentsAsObject(rawArguments, out arguments))
            {
                return new FunctionCallContent(id, function.Name, arguments);
            }

            var normalizedCandidates = BuildNormalizedToolArgumentCandidates(rawArguments);
            foreach (var candidate in normalizedCandidates)
            {
                if (TryParseFunctionArgumentsAsObject(candidate, out arguments))
                {
                    Trace.TraceWarning(
                        "[{0}] Tool call arguments required normalization for function '{1}' (callId={2}).",
                        "vllm",
                        function.Name,
                        id);
                    return new FunctionCallContent(id, function.Name, arguments);
                }
            }

            throw new InvalidOperationException(
                $"Cannot create FunctionCallContent: function arguments is not valid JSON. callId={id}, functionName={function.Name}, argumentsLength={rawArguments.Length}.");
        }

        private protected static bool TryParseFunctionArgumentsAsObject(string rawArguments, out IDictionary<string, object?> arguments)
        {
            arguments = new Dictionary<string, object?>();
            try
            {
                using var doc = JsonDocument.Parse(rawArguments);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                arguments = JsonConvert.DeserializeObject<IDictionary<string, object?>>(rawArguments)
                    ?? new Dictionary<string, object?>();
                return true;
            }
            catch (System.Text.Json.JsonException ex)
            {
                Trace.TraceWarning("[{0}] Tool call arguments JSON parse failed: {1}", "vllm", ex.Message);
                return false;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Trace.TraceWarning("[{0}] Tool call arguments JSON parse failed: {1}", "vllm", ex.Message);
                return false;
            }
        }

        private protected static IEnumerable<string> BuildNormalizedToolArgumentCandidates(string rawArguments)
        {
            var candidates = new List<string>();

            static void AddCandidate(List<string> list, string? candidate)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !list.Contains(candidate, StringComparer.Ordinal))
                {
                    list.Add(candidate);
                }
            }

            var trimmed = rawArguments.Trim();
            AddCandidate(candidates, trimmed);

            var wrapped = $"{{{trimmed.Trim(',')}}}";
            AddCandidate(candidates, wrapped);

            foreach (var baseCandidate in candidates.ToArray())
            {
                var fixedDanglingQuote = Regex.Replace(
                    baseCandidate,
                    @"(?<prefix>[{,]\s*)(?<key>[A-Za-z_][A-Za-z0-9_-]*)""\s*:",
                    "${prefix}\"${key}\":");
                AddCandidate(candidates, fixedDanglingQuote);

                var fixedUnquotedKeys = Regex.Replace(
                    fixedDanglingQuote,
                    @"(?<prefix>[{,]\s*)(?<key>[A-Za-z_][A-Za-z0-9_-]*)(?<suffix>\s*:)",
                    "${prefix}\"${key}\"${suffix}");
                AddCandidate(candidates, fixedUnquotedKeys);
            }

            var keyValueFragment = Regex.Match(trimmed, @"^\s*""?(?<key>[A-Za-z_][A-Za-z0-9_-]*)""?\s*:\s*(?<value>.+?)\s*$", RegexOptions.Singleline);
            if (keyValueFragment.Success)
            {
                var key = keyValueFragment.Groups["key"].Value;
                var value = keyValueFragment.Groups["value"].Value.Trim();
                if (!LooksLikeJsonValue(value))
                {
                    value = JsonSerializer.Serialize(value.Trim('"'));
                }

                AddCandidate(candidates, $"{{\"{key}\":{value}}}");
            }

            AddCandidate(candidates, $"{{\"input\":{JsonSerializer.Serialize(trimmed)}}}");
            return candidates;
        }

        private protected static bool LooksLikeJsonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();
            return value.StartsWith("\"", StringComparison.Ordinal)
                   || value.StartsWith("{", StringComparison.Ordinal)
                   || value.StartsWith("[", StringComparison.Ordinal)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("null", StringComparison.OrdinalIgnoreCase)
                   || char.IsDigit(value[0])
                   || value[0] == '-';
        }

        private static bool IsMalformedToolCallProtocolException(InvalidOperationException ex)
        {
            var message = ex.Message ?? string.Empty;
            return message.Contains("empty function.name", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("function name is empty", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("function arguments is not valid JSON", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("function arguments JSON root must be object", StringComparison.OrdinalIgnoreCase);
        }

        protected static JsonElement? ToVllmChatResponseFormat(ChatResponseFormat? format)
        {
            if (format is ChatResponseFormatJson jsonFormat)
            {
                return jsonFormat.Schema ?? _schemalessJsonResponseFormatValue;
            }

            return null;
        }

        private protected virtual IEnumerable<ChatMessage> PrepareMessagesWithSkills(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var messageList = messages.ToList();

            if (options is not VllmChatOptions vllmOptions)
            {
                return messageList;
            }

            if (!vllmOptions.EnableSkills && string.IsNullOrWhiteSpace(vllmOptions.SkillDirectoryPath))
            {
                return messageList;
            }

            // Inject built-in skill AIFunctions into options.Tools so that
            // FunctionInvokingChatClient can find and execute them.
            var skillsDir = GetEffectiveSkillsDirectory(vllmOptions.SkillDirectoryPath);
            if (Directory.Exists(skillsDir))
            {
                var builtInFunctions = CreateBuiltInSkillTools(skillsDir).ToList();
                if (builtInFunctions.Count > 0)
                {
                    vllmOptions.Tools ??= [];
                    foreach (var fn in builtInFunctions)
                    {
                        if (!vllmOptions.Tools.OfType<AIFunction>().Any(t => t.Name == fn.Name))
                        {
                            vllmOptions.Tools.Add(fn);
                        }
                    }
                }
            }

            var skillCatalog = LoadSkillCatalog(vllmOptions.SkillDirectoryPath);
            if (string.IsNullOrWhiteSpace(skillCatalog?.Instruction))
            {
                return messageList;
            }

            var skillInstruction = skillCatalog.Instruction;

            if (messageList.Count > 0 && messageList[0].Role == ChatRole.System)
            {
                var existingSystemText = string.Join(
                    "\n",
                    messageList[0].Contents.OfType<TextContent>().Select(tc => tc.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));

                var mergedSystemText = string.IsNullOrWhiteSpace(existingSystemText)
                    ? skillInstruction
                    : $"{skillInstruction}\n\n{existingSystemText}";

                messageList[0] = new ChatMessage(ChatRole.System, mergedSystemText);
                return messageList;
            }

            return [new ChatMessage(ChatRole.System, skillInstruction), .. messageList];
        }

        private static SkillCatalog? LoadSkillCatalog(string? skillDirectoryPath)
        {
            var effectiveDirectory = string.IsNullOrWhiteSpace(skillDirectoryPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "skills")
                : Path.GetFullPath(skillDirectoryPath);

            return LoadSkillCatalogFromDirectory(effectiveDirectory);
        }

        private static SkillCatalog? LoadSkillCatalogFromDirectory(string effectiveDirectory)
        {
            try
            {
                if (!Directory.Exists(effectiveDirectory))
                {
                    return null;
                }

                var skillFiles = EnumerateSkillFiles(effectiveDirectory).ToArray();
                if (skillFiles.Length == 0)
                {
                    return null;
                }

                var lastWrite = skillFiles.Max(skill => File.GetLastWriteTimeUtc(skill.FullPath));

                if (_skillCatalogCache.TryGetValue(effectiveDirectory, out var cached)
                    && cached.timestamp >= lastWrite
                    && cached.catalog is not null)
                {
                    return cached.catalog;
                }

                var manifests = skillFiles
                    .Select(skill => TryLoadSkillManifest(skill.FullPath, skill.RelativePath))
                    .Where(skill => skill is not null)
                    .Cast<SkillManifest>()
                    .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (manifests.Length == 0)
                {
                    return null;
                }

                var catalog = new SkillCatalog(BuildSkillCatalogInstruction(manifests), manifests);
                _skillCatalogCache[effectiveDirectory] = (catalog, lastWrite);
                return catalog;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<(string RelativePath, string FullPath)> EnumerateSkillFiles(string effectiveDirectory)
        {
            foreach (var file in Directory
                .EnumerateFiles(effectiveDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                yield return (Path.GetFileName(file)!, file);
            }

            foreach (var dir in Directory.EnumerateDirectories(effectiveDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                var skillMd = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillMd))
                {
                    yield return ($"{dirName}/SKILL.md", skillMd);
                    continue;
                }

                var skillMdLower = Path.Combine(dir, "skill.md");
                if (File.Exists(skillMdLower))
                {
                    yield return ($"{dirName}/skill.md", skillMdLower);
                }
            }
        }

        private static SkillManifest? TryLoadSkillManifest(string filePath, string relativePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                var fallbackName = GetFallbackSkillName(filePath, relativePath);
                var descriptionSource = content;
                string? name = null;
                string? description = null;

                var frontMatterMatch = Regex.Match(
                    content,
                    @"\A---\s*\r?\n(?<frontmatter>[\s\S]*?)\r?\n---\s*(?:\r?\n)?(?<body>[\s\S]*)\z",
                    RegexOptions.CultureInvariant);

                if (frontMatterMatch.Success)
                {
                    var frontMatter = frontMatterMatch.Groups["frontmatter"].Value;
                    descriptionSource = frontMatterMatch.Groups["body"].Value;
                    name = TryGetFrontMatterValue(frontMatter, "name");
                    description = TryGetFrontMatterValue(frontMatter, "description");
                }

                name = string.IsNullOrWhiteSpace(name) ? fallbackName : name.Trim();
                description = string.IsNullOrWhiteSpace(description)
                    ? BuildFallbackSkillDescription(descriptionSource)
                    : NormalizeSkillDescription(description);

                if (string.IsNullOrWhiteSpace(description))
                {
                    description = $"Use this skill when the task relates to {name}.";
                }

                return new SkillManifest(name, description, Path.GetFileName(filePath)!, relativePath, filePath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetFallbackSkillName(string filePath, string relativePath)
        {
            return Path.GetFileName(filePath).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(filePath).Equals("skill.md", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(Path.GetDirectoryName(filePath)) ?? Path.GetFileNameWithoutExtension(relativePath)
                : Path.GetFileNameWithoutExtension(relativePath);
        }

        private static string? TryGetFrontMatterValue(string frontMatter, string key)
        {
            var match = Regex.Match(
                frontMatter,
                $@"^\s*{Regex.Escape(key)}\s*:\s*(?<value>.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return null;
            }

            return match.Groups["value"].Value.Trim().Trim('\'', '"');
        }

        private static string? BuildFallbackSkillDescription(string content)
        {
            foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)
                    || line == "---"
                    || line.StartsWith("```", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    line = line.TrimStart('#').Trim();
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                return NormalizeSkillDescription(line);
            }

            return null;
        }

        private static string NormalizeSkillDescription(string description)
        {
            var normalized = Regex.Replace(description.Trim(), @"\s+", " ");
            return normalized.Length <= 240 ? normalized : normalized[..237] + "...";
        }

        private static string BuildSkillCatalogInstruction(IEnumerable<SkillManifest> manifests)
        {
            var sections = manifests
                .Select(skill => $"## Skill: {skill.Name}\nDescription: {skill.Description}\nFile: {skill.RelativePath}")
                .ToArray();

            return $"""
                # Skills

                You have {sections.Length} skill(s) available from the local skills directory.
                Only the skill metadata below is loaded into context right now.
                Based on the user's question, first select the most relevant skill by name and description.
                When you need the full instructions for a skill, call ReadSkillFile with the skill name or file name to load that skill's markdown content.
                If no skill is relevant, answer the question directly without referencing any skill.
                If multiple skills are relevant, read the necessary skill files and combine their instructions.

                You also have built-in tools:
                - ListSkillFiles: Lists available skills with their names, descriptions, and file names.
                - ReadSkillFile: Reads the full markdown content of a specific skill on demand.

                {string.Join("\n\n", sections)}
                """;
        }

        private static SkillManifest? ResolveSkillManifest(string skillsDir, string identifier)
        {
            var normalizedIdentifier = identifier.Trim();
            if (string.IsNullOrWhiteSpace(normalizedIdentifier))
            {
                return null;
            }

            var catalog = LoadSkillCatalogFromDirectory(skillsDir);
            if (catalog?.Skills is not { Length: > 0 } skills)
            {
                return null;
            }

            return skills
                .Select(skill => new
                {
                    Skill = skill,
                    Score =
                        string.Equals(skill.RelativePath, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ? 0 :
                        string.Equals(skill.FileName, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ? 1 :
                        string.Equals(skill.Name, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ? 2 :
                        Path.GetFileNameWithoutExtension(skill.FileName).Equals(normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ? 3 :
                        Path.GetDirectoryName(skill.RelativePath)?.Replace('\\', '/').TrimEnd('/').Equals(normalizedIdentifier, StringComparison.OrdinalIgnoreCase) == true ? 4 :
                        int.MaxValue
                })
                .Where(match => match.Score != int.MaxValue)
                .OrderBy(match => match.Score)
                .ThenBy(match => match.Skill.Name, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Skill)
                .FirstOrDefault();
        }

        private static bool TryGetPathUnderDirectory(string rootDirectory, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidatePath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));

            if (!candidatePath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidatePath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidatePath;
            return true;
        }

        private protected virtual VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            ValidateMessages(messages, options);

            VllmOpenAIChatRequest request = new()
            {
                Format = ToVllmChatResponseFormat(options?.ResponseFormat),
                Messages = PrepareMessagesWithSkills(messages, options).SelectMany(ToVllmChatRequestMessages).ToArray(),
                Model = options?.ModelId ?? _metadata.DefaultModelId ?? string.Empty,
                Stream = stream,
                StreamOptions = stream ? new VllmStreamOptions { IncludeUsage = true } : null,
                Tools = GetTools(options),
            };

            if (options?.ToolMode is AutoChatToolMode)
            {
                request.ToolChoice = "auto";
            }
            else if (options?.ToolMode is RequiredChatToolMode required)
            {
                if (string.IsNullOrEmpty(required.RequiredFunctionName))
                {
                    request.ToolChoice = "required";
                }
                else
                {
                    request.ToolChoice = new { type = "function", function = new { name = required.RequiredFunctionName } };
                }
            }
            else if (options?.ToolMode is NoneChatToolMode)
            {
                request.ToolChoice = "none";
            }

            ApplyRequestOptions(request, options);
            return request;
        }

        private protected virtual IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            if (content.Role == ChatRole.Tool)
            {
                foreach (var item in content.Contents)
                {
                    if (item is FunctionResultContent frc)
                    {
                        string resultStr = frc.Result is string s
                            ? s
                            : JsonSerializer.Serialize(frc.Result,
                                  ToolCallJsonSerializerOptions.GetTypeInfo(typeof(object)));

                        yield return new VllmOpenAIChatRequestMessage
                        {
                            Role = "tool",
                            ToolCallId = frc.CallId,
                            Content = resultStr
                        };
                    }
                }
                yield break;
            }

            var toolCalls = new List<VllmToolCall>();
            var images = new List<string>();
            var sb = new System.Text.StringBuilder();

            foreach (var item in content.Contents)
            {
                if (item is DataContent dataContent && dataContent.HasTopLevelMediaType("image"))
                {
                    images.Add(Convert.ToBase64String(dataContent.Data
#if NET
                        .Span));
#else
                        .ToArray()));
#endif
                }
                else if (item is TextContent textContent)
                {
                    sb.Append(textContent.Text);
                }
                else if (item is FunctionCallContent fcc)
                {
                    toolCalls.Add(new VllmToolCall
                    {
                        Id = fcc.CallId,
                        Type = "function",
                        Function = new VllmFunctionToolCall
                        {
                            Name = fcc.Name,
                            Arguments = JsonSerializer.Serialize(
                                fcc.Arguments,
                                ToolCallJsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))
                        }
                    });
                }
            }

            yield return new VllmOpenAIChatRequestMessage
            {
                Role = content.Role.Value,
                Content = sb.Length > 0 ? sb.ToString() : (toolCalls.Count > 0 ? string.Empty : null),
                Images = images.Count > 0 ? images : null,
                ToolCalls = toolCalls.Count > 0 ? toolCalls.ToArray() : null
            };
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

            if (options.MaxOutputTokens is int maxTokens)
            {
                request.MaxTokens = maxTokens;
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

        private static string GetEffectiveSkillsDirectory(string? skillDirectoryPath)
        {
            return string.IsNullOrWhiteSpace(skillDirectoryPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "skills")
                : Path.GetFullPath(skillDirectoryPath);
        }

        private static IEnumerable<AIFunction> CreateBuiltInSkillTools(string skillsDir)
        {
            yield return AIFunctionFactory.Create(
                [Description("List all available skill files (.md) in the skills directory, including subdirectory skills.")]
                () =>
                {
                    if (!Directory.Exists(skillsDir))
                    {
                        return "No skills directory found.";
                    }

                    var manifests = LoadSkillCatalogFromDirectory(skillsDir)?.Skills;
                    return manifests is { Length: > 0 }
                        ? string.Join(
                            "\n",
                            manifests.Select(skill => $"{skill.Name} | {skill.RelativePath} | {skill.Description}"))
                        : "No skill files found.";
                },
                "ListSkillFiles",
                "List available skills with their names, descriptions, and file names.");

            yield return AIFunctionFactory.Create(
                [Description("Read the full content of a specific skill file. For top-level files, use filename directly. For subdirectory skills, use 'dirname/SKILL.md' format.")]
                ([Description("The name of the skill file to read")] string? fileName = null) =>
                {
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        return "Error: No filename provided.";
                    }
                    var manifest = ResolveSkillManifest(skillsDir, fileName);
                    if (manifest is not null)
                    {
                        return File.ReadAllText(manifest.FullPath);
                    }

                    if (!TryGetPathUnderDirectory(skillsDir, fileName, out var filePath))
                    {
                        return $"File '{fileName}' not found.";
                    }

                    if (!File.Exists(filePath))
                    {
                        var subDirSkill = Path.Combine(filePath, "SKILL.md");
                        if (File.Exists(subDirSkill))
                        {
                            return File.ReadAllText(subDirSkill);
                        }

                        var subDirSkillLower = Path.Combine(filePath, "skill.md");
                        if (File.Exists(subDirSkillLower))
                        {
                            return File.ReadAllText(subDirSkillLower);
                        }
                        return $"File '{fileName}' not found.";
                    }

                    return File.ReadAllText(filePath);
                },
                "ReadSkillFile",
                "Read the full content of a specific skill file. For top-level files, use filename directly. For subdirectory skills, use directory name.");

            yield return AIFunctionFactory.Create(
                [Description("Create or overwrite a skill file. The content should be the full markdown content of the skill.")]
                (string fileName, string content) =>
                {
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        return "Error: No filename provided.";
                    }
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return "Error: No content provided.";
                    }

                    try
                    {
                        if (!TryGetPathUnderDirectory(skillsDir, fileName, out var filePath))
                        {
                            return $"Error creating skill file: '{fileName}' is outside the skills directory.";
                        }

                        var directory = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(filePath, content);
                        
                        // Invalidate cache for this directory so the new skill is picked up immediately
                        _skillCatalogCache.TryRemove(skillsDir, out _);

                        return $"Skill file '{fileName}' created successfully.";
                    }
                    catch (Exception ex)
                    {
                        return $"Error creating skill file: {ex.Message}";
                    }
                },
                "CreateSkillFile",
                "Create a new skill file with the specified content.");
        }
    }
}
