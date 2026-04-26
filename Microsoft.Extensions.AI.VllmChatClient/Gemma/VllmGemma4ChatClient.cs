using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Shared.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    public class VllmGemma4ChatClient : VllmBaseChatClient
    {
        [ThreadStatic]
        private static Dictionary<string, string>? _functionCallNameMap;

        private readonly bool _useGoogleNativeApi;

        public VllmGemma4ChatClient(string endpoint, string? token = null, string? modelId = "gemma-4-31b-it", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(ProcessEndpoint(endpoint, modelId), token, modelId, httpClient, apiMode)
        {
            _useGoogleNativeApi = IsGoogleNativeEndpoint(endpoint);

            if (!string.IsNullOrWhiteSpace(token))
            {
                HttpClient.DefaultRequestHeaders.Remove("x-goog-api-key");
                HttpClient.DefaultRequestHeaders.Remove("Authorization");

                if (_useGoogleNativeApi)
                {
                    HttpClient.DefaultRequestHeaders.Add("x-goog-api-key", token);
                }
                else
                {
                    HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }

        protected override bool EnableLegacyToolCallTextFallback(ChatOptions? options)
            => options?.ToolMode is not NoneChatToolMode
               && options?.Tools is { Count: > 0 };

        private static string ProcessEndpoint(string endpoint, string? modelId)
        {
            _ = Throw.IfNull(endpoint);

            endpoint = endpoint.Trim();
            if (endpoint.EndsWith("/", StringComparison.Ordinal))
            {
                endpoint = endpoint.TrimEnd('/');
            }

            if (IsGoogleNativeEndpoint(endpoint))
            {
                if (endpoint.Contains(":generateContent", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Contains(":streamGenerateContent", StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint;
                }

                if (endpoint.Contains("/v1beta", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Contains("/v1alpha", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Contains("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{endpoint}/models/{modelId ?? "gemma-4-31b-it"}:generateContent";
                }

                return endpoint;
            }

            if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            endpoint = endpoint
                .Replace("{0}", "v1", StringComparison.Ordinal)
                .Replace("{1}", string.Empty, StringComparison.Ordinal)
                .TrimEnd('/');

            if (endpoint.Contains("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/chat/completions";
            }

            return endpoint + "/{0}/{1}";
        }

        private static bool IsGoogleNativeEndpoint(string endpoint)
            => endpoint.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase)
               || endpoint.Contains(":generateContent", StringComparison.OrdinalIgnoreCase)
               || endpoint.Contains(":streamGenerateContent", StringComparison.OrdinalIgnoreCase);

        private protected override void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            base.ApplyRequestOptions(request, options);

            request.Reasoning = new VllmReasoningOptions
            {
                Enabled = options is VllmChatOptions vllmOptions
                    ? vllmOptions.ThinkingEnabled
                    : true
            };
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
            => base.ToVllmChatRequest(messages, options, stream);

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (!_useGoogleNativeApi)
            {
                var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
                response = NormalizeEmbeddedThoughtIfNeeded(response);
                response = NormalizeFencedJsonIfNeeded(response);
                return HideReasoningIfNeeded(response, options);
            }

            _ = Throw.IfNull(messages);
            var apiEndpoint = ApiChatEndpoint.Replace(":streamGenerateContent", ":generateContent", StringComparison.OrdinalIgnoreCase);
            var request = ToGoogleNativeRequest(messages, options);

            using var httpResponse = await HttpClient.PostAsJsonAsync(apiEndpoint, request, cancellationToken).ConfigureAwait(false);
            if (!httpResponse.IsSuccessStatusCode)
            {
                await VllmUtilities.ThrowUnsuccessfulVllmResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            var geminiResponse = await httpResponse.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken).ConfigureAwait(false);
            if (geminiResponse == null)
            {
                throw new InvalidOperationException("Google native API returned an empty response.");
            }

            return HideReasoningIfNeeded(FromGoogleNativeResponse(geminiResponse, options), options);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_useGoogleNativeApi)
            {
                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
                {
                    if (!ShouldExposeReasoning(options) && update is ReasoningChatResponseUpdate { Thinking: true })
                    {
                        continue;
                    }

                    yield return update;
                }

                yield break;
            }

            _ = Throw.IfNull(messages);
            var apiEndpoint = ApiChatEndpoint.Replace(":generateContent", ":streamGenerateContent", StringComparison.OrdinalIgnoreCase);
            var requestPayload = ToGoogleNativeRequest(messages, options);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AppendAltSse(apiEndpoint))
            {
                Content = JsonContent.Create(requestPayload)
            };

            using var httpResponse = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

#if NET
            while ((await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { } line)
#else
            while ((await streamReader.ReadLineAsync().ConfigureAwait(false)) is { } line)
#endif
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var jsonData = line[6..];
                if (string.IsNullOrWhiteSpace(jsonData) || jsonData == "[DONE]")
                {
                    continue;
                }

                GeminiResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<GeminiResponse>(jsonData);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (chunk?.Candidates is not { Length: > 0 })
                {
                    continue;
                }

                var candidate = chunk.Candidates[0];
                var finishReason = candidate.FinishReason switch
                {
                    "STOP" => ChatFinishReason.Stop,
                    "MAX_TOKENS" => ChatFinishReason.Length,
                    _ => (ChatFinishReason?)null
                };

                foreach (var part in candidate.Content?.Parts ?? [])
                {
                    if (part.Thought == true && !string.IsNullOrEmpty(part.Text))
                    {
                        if (ShouldExposeReasoning(options))
                        {
                            yield return new ReasoningChatResponseUpdate
                            {
                                CreatedAt = DateTimeOffset.UtcNow,
                                FinishReason = null,
                                ModelId = options?.ModelId ?? Metadata.DefaultModelId,
                                ResponseId = responseId,
                                Role = ChatRole.Assistant,
                                Thinking = true,
                                Reasoning = part.Text,
                                Contents = new List<AIContent> { new TextContent(part.Text) }
                            };
                        }

                        continue;
                    }

                    if (part.FunctionCall != null)
                    {
#if NET
                        var callId = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                        var callId = Guid.NewGuid().ToString().Substring(0, 8);
#endif
                        var args = part.FunctionCall.Args ?? new Dictionary<string, object?>();
                        _functionCallNameMap ??= new();
                        _functionCallNameMap[callId] = part.FunctionCall.Name ?? string.Empty;

                        yield return new ReasoningChatResponseUpdate
                        {
                            CreatedAt = DateTimeOffset.UtcNow,
                            FinishReason = ChatFinishReason.ToolCalls,
                            ModelId = options?.ModelId ?? Metadata.DefaultModelId,
                            ResponseId = responseId,
                            Role = ChatRole.Assistant,
                            Thinking = false,
                            Reasoning = string.Empty,
                            Contents = new List<AIContent>
                            {
                                new FunctionCallContent(callId, part.FunctionCall.Name ?? string.Empty, args)
                            }
                        };
                    }

                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        yield return new ReasoningChatResponseUpdate
                        {
                            CreatedAt = DateTimeOffset.UtcNow,
                            FinishReason = finishReason,
                            ModelId = options?.ModelId ?? Metadata.DefaultModelId,
                            ResponseId = responseId,
                            Role = ChatRole.Assistant,
                            Thinking = false,
                            Reasoning = string.Empty,
                            Contents = new List<AIContent> { new TextContent(part.Text) }
                        };
                    }
                }
            }
        }

        private static bool ShouldExposeReasoning(ChatOptions? options)
            => options is not VllmChatOptions vllmOptions || vllmOptions.ThinkingEnabled;

        private static Uri AppendAltSse(string endpoint)
        {
            var builder = new UriBuilder(endpoint);
            var query = System.Web.HttpUtility.ParseQueryString(builder.Query);
            query["alt"] = "sse";
            builder.Query = query.ToString();
            return builder.Uri;
        }

        private ChatResponse HideReasoningIfNeeded(ChatResponse response, ChatOptions? options)
        {
            if (ShouldExposeReasoning(options) || response is not ReasoningChatResponse reasoningResponse || response.Messages.Count == 0)
            {
                return response;
            }

            return new ReasoningChatResponse(response.Messages[0], string.Empty)
            {
                CreatedAt = reasoningResponse.CreatedAt,
                FinishReason = reasoningResponse.FinishReason,
                ModelId = reasoningResponse.ModelId,
                ResponseId = reasoningResponse.ResponseId,
                Usage = reasoningResponse.Usage,
            };
        }

        private static ChatResponse NormalizeFencedJsonIfNeeded(ChatResponse response)
        {
            if (response.Messages.Count == 0 || !TryUnwrapJsonCodeFence(response.Messages[0].Text, out var jsonText))
            {
                return response;
            }

            if (response is not ReasoningChatResponse reasoningResponse)
            {
                return response;
            }

            var message = response.Messages[0];
            var contents = message.Contents
                .Where(static content => content is not TextContent)
                .ToList();
            contents.Add(new TextContent(jsonText));

            return new ReasoningChatResponse(new ChatMessage(message.Role, contents), reasoningResponse.Reason)
            {
                CreatedAt = reasoningResponse.CreatedAt,
                FinishReason = reasoningResponse.FinishReason,
                ModelId = reasoningResponse.ModelId,
                ResponseId = reasoningResponse.ResponseId,
                Usage = reasoningResponse.Usage,
            };
        }

        private static bool TryUnwrapJsonCodeFence(string? text, out string jsonText)
        {
            jsonText = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                return false;
            }

            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd < 0)
            {
                return false;
            }

            var openingLine = trimmed[..firstLineEnd].Trim();
            if (!openingLine.Equals("```", StringComparison.Ordinal)
                && !openingLine.Equals("```json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var closingFenceStart = trimmed.LastIndexOf("\n```", StringComparison.Ordinal);
            if (closingFenceStart <= firstLineEnd)
            {
                return false;
            }

            var payload = trimmed[(firstLineEnd + 1)..closingFenceStart].Trim();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            try
            {
                using var _ = JsonDocument.Parse(payload);
                jsonText = payload;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static ChatResponse NormalizeEmbeddedThoughtIfNeeded(ChatResponse response)
        {
            if (response is not ReasoningChatResponse reasoningResponse || response.Messages.Count == 0)
            {
                return response;
            }

            var message = response.Messages[0];
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text)
                || !string.IsNullOrWhiteSpace(reasoningResponse.Reason)
                || !TryExtractEmbeddedThought(text, out var extractedReasoning, out var extractedAnswer))
            {
                return response;
            }

            var contents = message.Contents
                .Where(static content => content is not TextContent)
                .ToList();

            if (!string.IsNullOrWhiteSpace(extractedAnswer))
            {
                contents.Add(new TextContent(extractedAnswer));
            }

            return new ReasoningChatResponse(new ChatMessage(message.Role, contents), extractedReasoning)
            {
                CreatedAt = reasoningResponse.CreatedAt,
                FinishReason = reasoningResponse.FinishReason,
                ModelId = reasoningResponse.ModelId,
                ResponseId = reasoningResponse.ResponseId,
                Usage = reasoningResponse.Usage,
            };
        }

        private static bool TryExtractEmbeddedThought(string text, out string reasoning, out string answer)
        {
            reasoning = string.Empty;
            answer = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.Replace("\r\n", "\n").Trim();
            if (!normalized.StartsWith("thought", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = normalized["thought".Length..].TrimStart('\n', '\r', ' ', '\t', ':', '：');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var markerIndex = FindAnswerMarkerIndex(normalized);
            if (markerIndex >= 0)
            {
                reasoning = normalized[..markerIndex].Trim();
                answer = normalized[markerIndex..].Trim();
                return !string.IsNullOrWhiteSpace(reasoning);
            }

            var paragraphs = normalized
                .Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (paragraphs.Length >= 2 && LooksLikeFinalAnswerParagraph(paragraphs[^1]))
            {
                reasoning = string.Join(Environment.NewLine + Environment.NewLine, paragraphs[..^1]).Trim();
                answer = paragraphs[^1].Trim();
                return !string.IsNullOrWhiteSpace(reasoning);
            }

            if (TryExtractTrailingFencedJsonAnswer(normalized, out reasoning, out answer))
            {
                return true;
            }

            if (TryExtractTrailingInlineAnswer(normalized, out reasoning, out answer))
            {
                return true;
            }

            return false;
        }

        private static bool TryExtractTrailingFencedJsonAnswer(string text, out string reasoning, out string answer)
        {
            reasoning = string.Empty;
            answer = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var openingFenceIndex = text.LastIndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (openingFenceIndex < 0)
            {
                openingFenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            }

            if (openingFenceIndex <= 0)
            {
                return false;
            }

            var fencedBlock = text[openingFenceIndex..].Trim();
            if (!TryUnwrapJsonCodeFence(fencedBlock, out _))
            {
                return false;
            }

            var prefix = text[..openingFenceIndex].TrimEnd();
            if (string.IsNullOrWhiteSpace(prefix) || !LooksLikeReasoningPrefix(prefix))
            {
                return false;
            }

            reasoning = prefix.Trim();
            answer = fencedBlock;
            return true;
        }

        private static bool TryExtractTrailingInlineAnswer(string text, out string reasoning, out string answer)
        {
            reasoning = string.Empty;
            answer = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lastLineStart = text.LastIndexOf('\n');
            lastLineStart = lastLineStart < 0 ? 0 : lastLineStart + 1;

            var lastLine = text[lastLineStart..];
            var trimmedLastLine = lastLine.TrimStart();
            if (!(trimmedLastLine.StartsWith("*", StringComparison.Ordinal)
                || trimmedLastLine.StartsWith("-", StringComparison.Ordinal)
                || trimmedLastLine.StartsWith("•", StringComparison.Ordinal)))
            {
                return false;
            }

            foreach (var splitIndexInLine in EnumerateInlineAnswerBoundaryCandidates(lastLine))
            {
                if (splitIndexInLine + 1 >= lastLine.Length)
                {
                    continue;
                }

                var trailing = lastLine[(splitIndexInLine + 1)..].Trim();
                if (!LooksLikeFinalAnswerParagraph(trailing))
                {
                    continue;
                }

                var answerStart = lastLineStart + splitIndexInLine + 1;
                reasoning = text[..answerStart].Trim();
                answer = text[answerStart..].Trim();
                return !string.IsNullOrWhiteSpace(reasoning) && !string.IsNullOrWhiteSpace(answer);
            }

            return false;
        }

        private static IEnumerable<int> EnumerateInlineAnswerBoundaryCandidates(string lastLine)
        {
            var seen = new HashSet<int>();

            var preferred = new[]
            {
                lastLine.LastIndexOf(')'),
                lastLine.LastIndexOf('"'),
                lastLine.LastIndexOf('.'),
                lastLine.LastIndexOf('!'),
                lastLine.LastIndexOf('?'),
                lastLine.LastIndexOf('。'),
                lastLine.LastIndexOf('！'),
                lastLine.LastIndexOf('？')
            };

            foreach (var index in preferred)
            {
                if (index >= 0 && seen.Add(index))
                {
                    yield return index;
                }
            }
        }

        private static int FindAnswerMarkerIndex(string text)
        {
            string[] markers = ["\nassistant\n", "\nfinal\n", "\nanswer\n", "\n答案：", "\n答案:", "\n最终回答：", "\n最终回答:"];
            foreach (var marker in markers)
            {
                var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return index + marker.Length;
                }
            }

            return -1;
        }

        private static bool LooksLikeFinalAnswerParagraph(string paragraph)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                return false;
            }

            paragraph = paragraph.Trim();
            return !(paragraph.StartsWith("*", StringComparison.Ordinal)
                || paragraph.StartsWith("-", StringComparison.Ordinal)
                || paragraph.StartsWith("•", StringComparison.Ordinal)
                || paragraph.StartsWith("Option", StringComparison.OrdinalIgnoreCase)
                || paragraph.StartsWith("User question", StringComparison.OrdinalIgnoreCase)
                || paragraph.StartsWith("Role:", StringComparison.OrdinalIgnoreCase)
                || paragraph.StartsWith("System instruction", StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeReasoningPrefix(string prefix)
        {
            prefix = prefix.TrimEnd();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return false;
            }

            return prefix.StartsWith("*", StringComparison.Ordinal)
                || prefix.StartsWith("-", StringComparison.Ordinal)
                || prefix.StartsWith("•", StringComparison.Ordinal)
                || prefix.Contains("\n*", StringComparison.Ordinal)
                || prefix.Contains("\n-", StringComparison.Ordinal)
                || prefix.Contains("\n•", StringComparison.Ordinal)
                || prefix.EndsWith(".", StringComparison.Ordinal)
                || prefix.EndsWith("!", StringComparison.Ordinal)
                || prefix.EndsWith("?", StringComparison.Ordinal)
                || prefix.EndsWith("。", StringComparison.Ordinal)
                || prefix.EndsWith("！", StringComparison.Ordinal)
                || prefix.EndsWith("？", StringComparison.Ordinal);
        }

        private GeminiRequest ToGoogleNativeRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var request = new GeminiRequest
            {
                Contents = messages.Select(ToGoogleNativeContent).ToArray()
            };

            var generationConfig = new GeminiGenerationConfig();
            bool hasGenerationConfig = false;
            bool thinkingEnabled = options is VllmChatOptions vllmOptions ? vllmOptions.ThinkingEnabled : true;
            if (thinkingEnabled)
            {
                generationConfig.ThinkingConfig = new GeminiThinkingConfig
                {
                    ThinkingLevel = "HIGH"
                };
                hasGenerationConfig = true;
            }

            if (options?.Temperature is float temperature)
            {
                generationConfig.Temperature = temperature;
                hasGenerationConfig = true;
            }

            if (options?.TopP is float topP)
            {
                generationConfig.TopP = topP;
                hasGenerationConfig = true;
            }

            if (options?.MaxOutputTokens is int maxOutputTokens)
            {
                generationConfig.MaxOutputTokens = maxOutputTokens;
                hasGenerationConfig = true;
            }

            if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
            {
                generationConfig.ResponseMimeType = "application/json";
                if (jsonFormat.Schema is JsonElement schema)
                {
                    generationConfig.ResponseJsonSchema = schema;
                }
                hasGenerationConfig = true;
            }

            request.GenerationConfig = hasGenerationConfig ? generationConfig : null;

            if (options?.Tools is { Count: > 0 } tools && options.ToolMode is not NoneChatToolMode)
            {
                request.Tools =
                [
                    new GeminiTool
                    {
                        FunctionDeclarations = tools
                            .OfType<AIFunction>()
                            .Select(tool => new GeminiFunctionDeclaration
                            {
                                Name = tool.Name,
                                Description = tool.Description,
                                Parameters = CleanAndNormalizeSchema(tool.JsonSchema)
                            })
                            .ToArray()
                    }
                ];
            }

            return request;
        }

        private GeminiContent ToGoogleNativeContent(ChatMessage message)
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
                        parts.Add(new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = dataContent.MediaType ?? "image/jpeg",
                                Data = Convert.ToBase64String(dataContent.Data
#if NET
                                    .Span)
#else
                                    .ToArray())
#endif
                            }
                        });
                        break;

                    case FunctionCallContent functionCall:
                        _functionCallNameMap ??= new();
                        _functionCallNameMap[functionCall.CallId] = functionCall.Name;
                        parts.Add(new GeminiPart
                        {
                            FunctionCall = new GeminiFunctionCall
                            {
                                Name = functionCall.Name,
                                Args = functionCall.Arguments as Dictionary<string, object?>
                                    ?? new Dictionary<string, object?>(functionCall.Arguments ?? new Dictionary<string, object?>())
                            }
                        });
                        break;

                    case FunctionResultContent functionResult:
                        parts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Name = ResolveFunctionName(functionResult.CallId),
                                Response = BuildFunctionResponsePayload(functionResult.Result)
                            }
                        });
                        break;
                }
            }

            return new GeminiContent
            {
                Role = message.Role.Value switch
                {
                    "assistant" => "model",
                    "system" => "user",
                    "tool" => "function",
                    _ => "user"
                },
                Parts = parts.ToArray()
            };
        }

        private string ResolveFunctionName(string? callId)
        {
            if (!string.IsNullOrWhiteSpace(callId)
                && _functionCallNameMap?.TryGetValue(callId, out var name) == true
                && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return callId ?? string.Empty;
        }

        private static Dictionary<string, object?> BuildFunctionResponsePayload(object? result)
        {
            if (result is Dictionary<string, object?> dict)
            {
                return dict;
            }

            if (result is string text)
            {
                return new Dictionary<string, object?> { ["result"] = text };
            }

            if (result is null)
            {
                return new Dictionary<string, object?> { ["result"] = null };
            }

            var json = JsonSerializer.SerializeToElement(result);
            if (json.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(json.GetRawText(), AIJsonUtilities.DefaultOptions)
                    ?? new Dictionary<string, object?>();
            }

            return new Dictionary<string, object?> { ["result"] = JsonSerializer.Deserialize<object?>(json.GetRawText(), AIJsonUtilities.DefaultOptions) };
        }

        private ChatResponse FromGoogleNativeResponse(GeminiResponse response, ChatOptions? options)
        {
            if (response.Candidates is not { Length: > 0 })
            {
                throw new InvalidOperationException("Google native API returned no candidates.");
            }

            var candidate = response.Candidates[0];
            var contents = new List<AIContent>();
            var reasoningParts = new List<string>();

            foreach (var part in candidate.Content?.Parts ?? [])
            {
                if (part.Thought == true)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        reasoningParts.Add(part.Text);
                    }

                    continue;
                }

                if (!string.IsNullOrEmpty(part.Text))
                {
                    contents.Add(new TextContent(part.Text));
                }

                if (part.FunctionCall != null)
                {
#if NET
                    var callId = System.Security.Cryptography.RandomNumberGenerator.GetHexString(8);
#else
                    var callId = Guid.NewGuid().ToString().Substring(0, 8);
#endif
                    var args = part.FunctionCall.Args ?? new Dictionary<string, object?>();
                    _functionCallNameMap ??= new();
                    _functionCallNameMap[callId] = part.FunctionCall.Name ?? string.Empty;
                    contents.Add(new FunctionCallContent(callId, part.FunctionCall.Name ?? string.Empty, args));
                }
            }

            var message = new ChatMessage(ChatRole.Assistant, contents);
            return new ReasoningChatResponse(message, string.Join(Environment.NewLine, reasoningParts.Where(static text => !string.IsNullOrWhiteSpace(text))))
            {
                CreatedAt = DateTimeOffset.UtcNow,
                FinishReason = candidate.FinishReason switch
                {
                    "STOP" => ChatFinishReason.Stop,
                    "MAX_TOKENS" => ChatFinishReason.Length,
                    _ => null
                },
                ModelId = options?.ModelId ?? Metadata.DefaultModelId,
                ResponseId = Guid.NewGuid().ToString("N"),
                Usage = response.UsageMetadata is null
                    ? null
                    : new UsageDetails
                    {
                        InputTokenCount = response.UsageMetadata.PromptTokenCount,
                        OutputTokenCount = response.UsageMetadata.CandidatesTokenCount,
                        TotalTokenCount = response.UsageMetadata.TotalTokenCount
                    }
            };
        }

        private static object CleanAndNormalizeSchema(JsonElement schema)
        {
            try
            {
                return CleanSchemaElement(schema) ?? new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>()
                };
            }
            catch
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>()
                };
            }
        }

        private static object? CleanSchemaElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new Dictionary<string, object?>();
                    bool hasType = false;
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("type"))
                        {
                            hasType = true;
                            obj["type"] = property.Value.GetString() ?? "string";
                            continue;
                        }

                        obj[property.Name] = CleanSchemaElement(property.Value);
                    }

                    if (!hasType && obj.Count > 0 && !obj.ContainsKey("enum"))
                    {
                        if (obj.ContainsKey("properties"))
                        {
                            obj["type"] = "object";
                        }
                        else if (obj.ContainsKey("items"))
                        {
                            obj["type"] = "array";
                        }
                    }

                    return obj;

                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(CleanSchemaElement).ToList();

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var longValue))
                    {
                        return longValue;
                    }

                    if (element.TryGetDouble(out var doubleValue))
                    {
                        return doubleValue;
                    }

                    return null;

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                default:
                    return null;
            }
        }
    }
}
