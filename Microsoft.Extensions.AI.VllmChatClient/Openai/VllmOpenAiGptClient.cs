using Microsoft.Shared.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace Microsoft.Extensions.AI
{
    public class VllmOpenAiGptClient : VllmBaseChatClient
    {
        // OpenAI's own API (api.openai.com) rejects the OpenRouter/vLLM-style extensions this client adds for other hosts
        // (a nested "options" object, "reasoning", "include_reasoning", ...): it answers 400 unknown_parameter. For that host
        // the request carries only fields the official Chat Completions / Responses API defines.
        private static readonly VllmCompatibleEndpointProfile OfficialOpenAiProfile = new()
        {
            ProviderName = "openai",
            UseTopLevelGenerationOptions = true,
            UseMaxCompletionTokens = true,
        };

        private readonly bool _officialOpenAi;

        public VllmOpenAiGptClient(string endpoint, string? token = null, string? modelId = "openai/gpt-5.2-codex", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(NormalizeOpenAICompatibleEndpoint(endpoint, apiMode), token, modelId, httpClient, apiMode, IsOfficialOpenAiEndpoint(endpoint) ? OfficialOpenAiProfile : null)
        {
            _officialOpenAi = IsOfficialOpenAiEndpoint(endpoint);
        }

        // GPT-5/6 (not the -pro variants) accept reasoning effort "none"; o-series and -pro models do not.
        private static bool SupportsNoReasoning(string? model)
        {
            var name = ModelName(model);
            return (name.StartsWith("gpt-5", StringComparison.Ordinal) || name.StartsWith("gpt-6", StringComparison.Ordinal)) &&
                   !name.Contains("-pro", StringComparison.Ordinal);
        }

        // Reasoning models refuse temperature/top_p while they reason; with effort "none" they are accepted again.
        private static bool SamplingRefused(string? model, string? effort)
        {
            var name = ModelName(model);
            var reasoningFamily = name.StartsWith("gpt-5", StringComparison.Ordinal) ||
                                  name.StartsWith("gpt-6", StringComparison.Ordinal) ||
                                  name.StartsWith("o1", StringComparison.Ordinal) ||
                                  name.StartsWith("o3", StringComparison.Ordinal) ||
                                  name.StartsWith("o4", StringComparison.Ordinal);
            return reasoningFamily && !string.Equals(effort, "none", StringComparison.Ordinal);
        }

        private static string ModelName(string? model)
        {
            var name = (model ?? string.Empty).Trim().ToLowerInvariant();
            return name.StartsWith("openai/", StringComparison.Ordinal) ? name["openai/".Length..] : name;
        }

        // The Responses API takes structured output as text.format, not the Chat Completions response_format.
        private protected override VllmResponsesRequest ToVllmResponsesRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmResponsesRequest(messages, options, stream);

            // 所有主机都如此：GPT 模型的 Responses 接口（包括 OpenRouter 这类转发网关）只接受 text.format，
            // 对 response_format 以及 vLLM 专用的 structured_outputs 都会返回 400。
            if (request.ResponseFormat is { } format)
            {
                request.ResponseFormat = null;
                request.Text = new VllmResponsesTextOptions { Format = ToResponsesTextFormat(format) };
                request.ExtraBody?.Remove("structured_outputs");
                if (request.ExtraBody is { Count: 0 })
                {
                    request.ExtraBody = null;
                }
            }

            return request;
        }

        private static VllmResponsesTextFormat ToResponsesTextFormat(JsonElement chatFormat)
        {
            var type = chatFormat.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "json_object" : "json_object";
            if (type == "json_schema" && chatFormat.TryGetProperty("json_schema", out var schema))
            {
                return new VllmResponsesTextFormat
                {
                    Type = "json_schema",
                    Name = schema.TryGetProperty("name", out var name) ? name.GetString() : null,
                    Description = schema.TryGetProperty("description", out var description) ? description.GetString() : null,
                    Schema = schema.TryGetProperty("schema", out var body) ? body.Clone() : null,
                    Strict = schema.TryGetProperty("strict", out var strict) && strict.ValueKind is JsonValueKind.True or JsonValueKind.False ? strict.GetBoolean() : null,
                };
            }

            return new VllmResponsesTextFormat { Type = type };
        }

        internal static bool IsOfficialOpenAiEndpoint(string endpoint)
            => endpoint.Contains("//api.openai.com", StringComparison.OrdinalIgnoreCase);

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messagesList = messages as IList<ChatMessage>;

            bool continueLoop;
            do
            {
                continueLoop = false;
                bool hasToolCalls = false;
                int messageCountBefore = messagesList?.Count ?? -1;

                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    if (update.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        hasToolCalls = true;
                    }
                    yield return update;
                }

                if (hasToolCalls &&
                    messagesList is not null &&
                    messagesList.Count > messageCountBefore &&
                    messagesList[messagesList.Count - 1].Role == ChatRole.Tool)
                {
                    continueLoop = true;
                }
            } while (continueLoop);
        }

        private protected override void ApplyRequestOptions(VllmOpenAIChatRequest request, ChatOptions? options)
        {
            base.ApplyRequestOptions(request, options);

            if (_officialOpenAi)
            {
                // vLLM-only members the shared builder may have added (structured_outputs under extra_body, the Ollama-style "format").
                request.ExtraBody = null;
                request.Format = null;

                // Sampling and the token limit were moved to the top level by the profile. Reasoning effort is the only
                // reasoning control the official API takes, and only when the caller asked for one.
                var officialEffort = options switch
                {
                    OpenAiGptChatOptions gpt => gpt.ReasoningLevel switch
                    {
                        OpenAiGptReasoningLevel.Low => "low",
                        OpenAiGptReasoningLevel.High => "high",
                        _ => "medium"
                    },
                    VllmChatOptions { ReasoningEffort: { Length: > 0 } effort } when !string.IsNullOrWhiteSpace(effort) => effort,
                    // ThinkingEnabled=false means "do not reason". The GPT-5/6 families take that as effort "none", and for them it is
                    // also what makes function tools usable on /chat/completions (the API refuses tools with a reasoning effort there).
                    VllmChatOptions { ThinkingEnabled: false } when SupportsNoReasoning(request.Model) => "none",
                    // Without an explicit choice, function tools on /chat/completions still need "none" or the API answers 400.
                    not VllmChatOptions { ThinkingEnabled: true } when ApiMode != VllmApiMode.Responses && request.Tools?.Any() == true && SupportsNoReasoning(request.Model) => "none",
                    _ => null
                };

                if (officialEffort is not null)
                {
                    if (ApiMode == VllmApiMode.Responses)
                    {
                        request.Reasoning = new VllmReasoningOptions { Effort = officialEffort };
                    }
                    else
                    {
                        request.ReasoningEffort = officialEffort;
                    }
                }

                if (SamplingRefused(request.Model, officialEffort))
                {
                    request.Temperature = null;
                    request.TopP = null;
                }

                return;
            }

            if (options is VllmChatOptions vllmOptions && vllmOptions.ThinkingEnabled)
            {
                request.Reasoning = new VllmReasoningOptions
                {
                    Enabled = true
                };
            }

            if (options is OpenAiGptChatOptions openAiGptOptions)
            {
                var effort = openAiGptOptions.ReasoningLevel switch
                {
                    OpenAiGptReasoningLevel.Low => "low",
                    OpenAiGptReasoningLevel.Medium => "medium",
                    OpenAiGptReasoningLevel.High => "high",
                    _ => "medium"
                };

                request.Reasoning ??= new VllmReasoningOptions();
                request.Reasoning.Enabled = true;
                request.Reasoning.Effort = effort;
                request.Reasoning.Exclude = openAiGptOptions.ExcludeReasoning;
            }

            if (ApiMode == VllmApiMode.Responses)
            {
                // Responses 请求会把 extra_body 展开到顶层；那里的 reasoning 必须是对象，布尔值会被网关以 400 拒绝。
                // 思考配置由上面的 request.Reasoning（对象）携带。
                return;
            }

            (request.Options ??= new()).extra_body = new Dictionary<string, object?>
            {
                ["reasoning"] = true,
                ["include_reasoning"] = true,
                ["enable_reasoning"] = true,
                ["stream_reasoning"] = true
            };
        }



        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (ApiMode == VllmApiMode.Responses)
            {
                return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }

            _ = Throw.IfNull(messages);

            string apiEndpoint = GetChatEndpoint();

            using var httpResponse = await HttpClient.PostAsJsonAsync(
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

            string reason = responseMessage?.Reasoning ?? string.Empty;
            if (string.IsNullOrEmpty(reason) && responseMessage?.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail)
            {
                reason += detail.Text;
            }

            if (string.IsNullOrEmpty(reason))
            {
                var (hasReasoning, extractedText, _) = AnalyzeReasoningStructure(responseMessage?.ReasoningContent);
                if (hasReasoning)
                {
                    reason = extractedText;
                }
                else
                {
                    reason = responseMessage?.ReasoningContent?.ToString() ?? string.Empty;
                }
            }

            var retMessage = FromVllmMessage(responseMessage!, options);
            bool hasToolCall = retMessage.Contents.Any(c => c is FunctionCallContent);

            return new ReasoningChatResponse(retMessage, reason)
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = hasToolCall ? ChatFinishReason.ToolCalls : ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? Metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseOpenAiUsage(response),
            };
        }

        internal override ChatResponseUpdate? HandleStreamingReasoningContent(Delta delta, string responseId, string modelId)
        {
            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                return BuildTextUpdate(responseId, delta.Reasoning, true);
            }

            if (delta.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail && !string.IsNullOrEmpty(detail.Text))
            {
                return BuildTextUpdate(responseId, detail.Text, true);
            }

            if (delta.ReasoningContent != null)
            {
                var (hasReasoning, extractedText, _) = AnalyzeReasoningStructure(delta.ReasoningContent);
                if (hasReasoning && !string.IsNullOrEmpty(extractedText))
                {
                    return BuildTextUpdate(responseId, extractedText, true);
                }

                return BuildTextUpdate(responseId, delta.ReasoningContent, true);
            }

            return null;
        }

        private static UsageDetails? ParseOpenAiUsage(VllmChatResponse response)
        {
            // 与基类 Chat Completions 共用同一份计量解析（缺失为 null、缓存细项保留、非法计量统一报错）。
            return VllmBaseChatClient.ParseSharedChatCompletionsUsage(response);
        }

        private static (bool hasReasoning, string reasoningText, string reasoningType) AnalyzeReasoningStructure(object? reasoningContent)
        {
            if (reasoningContent == null)
            {
                return (false, string.Empty, "none");
            }

            bool hasReasoning = false;
            string reasoningText = string.Empty;
            string reasoningType = "none";

            try
            {
                if (reasoningContent is string stringContent && !string.IsNullOrEmpty(stringContent))
                {
                    hasReasoning = true;
                    reasoningText = stringContent;
                    reasoningType = "standard";
                }
                else if (reasoningContent is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("reasoning", out var reasoningProp))
                    {
                        if (reasoningProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(reasoningProp.GetString()))
                        {
                            hasReasoning = true;
                            reasoningText = reasoningProp.GetString()!;
                            reasoningType = "object_property";
                        }
                    }

                    if (jsonElement.TryGetProperty("reasoning_details", out var reasoningDetailsProp) &&
                        reasoningDetailsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in reasoningDetailsProp.EnumerateArray())
                        {
                            if (detail.TryGetProperty("type", out var typeProp) &&
                                typeProp.GetString() == "reasoning.text")
                            {
                                reasoningType = "structured";

                                if (string.IsNullOrEmpty(reasoningText) &&
                                    detail.TryGetProperty("text", out var textProp) &&
                                    textProp.ValueKind == JsonValueKind.String)
                                {
                                    hasReasoning = true;
                                    reasoningText = textProp.GetString()!;
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (reasoningContent != null)
                {
                    var jsonString = reasoningContent.ToString();
                    if (!string.IsNullOrEmpty(jsonString) && jsonString != "null" && jsonString != "{}")
                    {
                        hasReasoning = true;
                        reasoningText = jsonString;
                        reasoningType = "json";

                        try
                        {
                            var jsonDoc = JsonDocument.Parse(jsonString);
                            if (jsonDoc.RootElement.TryGetProperty("reasoning", out var rProp) &&
                                rProp.ValueKind == JsonValueKind.String)
                            {
                                reasoningText = rProp.GetString()!;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (reasoningContent != null)
                {
                    hasReasoning = true;
                    reasoningText = reasoningContent.ToString() ?? string.Empty;
                    reasoningType = "tostring";
                }
            }

            return (hasReasoning, reasoningText, reasoningType);
        }
    }
}
