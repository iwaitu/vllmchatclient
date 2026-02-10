using Microsoft.Shared.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace Microsoft.Extensions.AI
{
    public class VllmOpenAiGptClient : VllmBaseChatClient
    {
        public VllmOpenAiGptClient(string endpoint, string? token = null, string? modelId = "openai/gpt-5.2-codex", HttpClient? httpClient = null)
            : base(ProcessEndpoint(endpoint), token, modelId, httpClient)
        {
        }

        private static string ProcessEndpoint(string endpoint)
        {
            _ = Throw.IfNull(endpoint);
            if (endpoint.EndsWith("/"))
            {
                endpoint = endpoint.TrimEnd('/');
            }

            if (endpoint.Contains("/chat/completions"))
            {
                return endpoint;
            }
            else if (endpoint.Contains("/v1"))
            {
                return endpoint + "/chat/completions";
            }
            else
            {
                return endpoint + "/{0}/{1}";
            }
        }

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

            (request.Options ??= new()).extra_body = new Dictionary<string, object?>
            {
                ["reasoning"] = true,
                ["include_reasoning"] = true,
                ["enable_reasoning"] = true,
                ["stream_reasoning"] = true
            };
        }

        private protected override IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
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
                                string resultStr = frc.Result is string s
                                    ? s
                                    : System.Text.Json.JsonSerializer.Serialize(frc.Result,
                                          ToolCallJsonSerializerOptions.GetTypeInfo(typeof(object)));

                                yield return new VllmOpenAIChatRequestMessage
                                {
                                    Role = "tool",
                                    Name = "",
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

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
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

            if (response.Choices.Length == 0)
            {
                throw new InvalidOperationException("未返回任何响应选项。");
            }

            var responseMessage = response.Choices.FirstOrDefault()?.Message;

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
                    var jsonString = System.Text.Json.JsonSerializer.Serialize(reasoningContent);
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
