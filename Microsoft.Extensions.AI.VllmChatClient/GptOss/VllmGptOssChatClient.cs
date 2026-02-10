using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Reflection;

namespace Microsoft.Extensions.AI.VllmChatClient.GptOss
{
    public class VllmGptOssChatClient : VllmBaseChatClient
    {
        private static readonly string DefaultSystemPrompt = @"你必须遵循以下工具调用规则：
1) 当需要调用工具时，使用 tool_calls 格式返回工具调用。
2) 若需要多个工具，请在同一轮返回多个 tool_calls。
3) 若无法确定是否需要工具，先不调用工具，直接向用户询问澄清。";

        private static readonly string DefaultPromptMarker = "你必须遵循以下工具调用规则：";
        
        public VllmGptOssChatClient(string endpoint, string? token = null, string? modelId = "gpt-oss-120b", HttpClient? httpClient = null)
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

        private protected override IEnumerable<ChatMessage> PrepareMessagesWithSkills(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var newMessages = SetUpChatOptions(messages, options);
            return base.PrepareMessagesWithSkills(newMessages, options);
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

            // GptOss specific extra_body
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
                                // GPT-OSS-120b EXPECTS STANDARD OPENAI TOOL CALLS
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

            // SetUpChatOptions is called via PrepareMessagesWithSkills -> ToVllmChatRequest
            
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
            string reasoning = response.Choices.FirstOrDefault()?.Message?.Reasoning ?? ""; 

            // Use base implementation of FromVllmMessage to parse tool calls/content
            // Note: Base implementation handles XML tool calls, but here we expect OpenAI tool calls.
            // Wait, Base FromVllmMessage handles VllmChatResponseMessage which has standard ToolCalls array.
            // So FromVllmMessage in base should work for standard tool calls too.
            // Let's verify Base.FromVllmMessage:
            // "foreach (var toolcall in message.ToolCalls ?? []) { ... contents.Add(ToFunctionCallContent(...)) }"
            // Yes, it handles standard tool calls.
            
            var retMessage = FromVllmMessage(responseMessage!, options);

            return new ReasoningChatResponse(retMessage, reasoning)
            {
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(response.Created).UtcDateTime,
                FinishReason = ToFinishReason(response.Choices.FirstOrDefault()?.FinishReason),
                ModelId = response.Model ?? options?.ModelId ?? Metadata.DefaultModelId,
                ResponseId = response.Id,
                Usage = ParseGptOssUsage(response),
            };
        }

        internal override ChatResponseUpdate? HandleStreamingReasoningContent(Delta delta, string responseId, string modelId)
        {
            string reasoningText = "";
            string reasoningType = "unknown";
            
            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                reasoningText = delta.Reasoning;
                reasoningType = "standard";
            }
            else if (delta.ReasoningDetails?.Length > 0)
            {
                var reasoningDetail = delta.ReasoningDetails.FirstOrDefault(d => d.Type == "reasoning.text");
                if (reasoningDetail != null && !string.IsNullOrEmpty(reasoningDetail.Text))
                {
                    reasoningText = reasoningDetail.Text;
                    reasoningType = "structured";
                }
            }
            else if (delta.ReasoningContent != null)
            {
                var (hasReasoning, extractedText, extractedType) = AnalyzeReasoningStructure(delta.ReasoningContent);
                if (hasReasoning)
                {
                    reasoningText = extractedText;
                    reasoningType = extractedType;
                }
            }
            
            if (!string.IsNullOrEmpty(reasoningText))
            {
                return new ReasoningChatResponseUpdate
                {
                    CreatedAt = DateTimeOffset.Now,
                    ModelId = modelId,
                    ResponseId = responseId,
                    Role = ChatRole.Assistant,
                    Thinking = true,
                    Contents = new List<AIContent> { new TextContent(reasoningText) },
                    Reasoning = reasoningText
                };
            }

            return null;
        }

        private IEnumerable<ChatMessage> SetUpChatOptions(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var messagesList = messages.ToList();
            var systemMessages = messagesList.Where(m => m.Role == ChatRole.System).ToList();
            if (systemMessages.Count > 1)
            {
                throw new ArgumentException("Messages 中只能包含一条 system message。", nameof(messages));
            }

            var systemMessageIndex = messagesList.FindIndex(m => m.Role == ChatRole.System);
            bool hasExistingSystemMessage = systemMessageIndex >= 0;
            bool alreadyHasDefaultPrompt = false;

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
                
                alreadyHasDefaultPrompt = existingContent.Contains(DefaultPromptMarker);
                
                if (!alreadyHasDefaultPrompt)
                {
                    var defaultSystemMessage = DefaultSystemPrompt;
                    
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
                    
                    var combinedSystemMessage = defaultSystemMessage;
                    if (!string.IsNullOrWhiteSpace(existingContent.Trim()))
                    {
                        combinedSystemMessage += $"\n\n# Additional Instructions\n{existingContent.Trim()}";
                    }
                    
                    var newSystemMessage = new ChatMessage(ChatRole.System, new List<AIContent> { new TextContent(combinedSystemMessage) });
                    messagesList[systemMessageIndex] = newSystemMessage;
                }
            }
            else
            {
                var defaultSystemMessage = DefaultSystemPrompt;
                
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

            if (options is GptOssChatOptions && options.Tools?.Count > 0)
            {
                options.ToolMode = ChatToolMode.Auto;
            }

            return messagesList;
        }

        private class FunctionCallState
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public StringBuilder Arguments { get; set; } = new();
        }

        private ChatResponseUpdate CreateToolCallUpdate(string responseId, string modelId, FunctionCallState state)
        {
            var functionCall = new VllmFunctionToolCall
            {
                Name = state.Name,
                Arguments = state.Arguments.ToString()
            };
            
            return BuildToolCallUpdate(responseId, functionCall); 
        }

        private static bool IsJsonComplete(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;
            
            try
            {
                using var document = JsonDocument.Parse(json);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        private static UsageDetails? ParseGptOssUsage(VllmChatResponse response)
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

        private static (bool hasReasoning, string reasoningText, string reasoningType) AnalyzeReasoningStructure(object? reasoningContent)
        {
            if (reasoningContent == null)
            {
                return (false, "", "none");
            }

            bool hasReasoning = false;
            string reasoningText = "";
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
                    reasoningText = reasoningContent.ToString() ?? "";
                    reasoningType = "tostring";
                }
            }

            return (hasReasoning, reasoningText, reasoningType);
        }
    }
}
