using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    public class VllmQwen3NextChatClient : VllmBaseChatClient
    {
        protected override bool EnableLegacyToolCallTextFallback(ChatOptions? options) => true;

        private bool UseAliyunThinkingParameter()
        {
            var endpoint = ApiChatEndpoint
                .Replace("{0}", "v1", StringComparison.Ordinal)
                .Replace("{1}", "chat/completions", StringComparison.Ordinal);

            return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                && uri.Host.EndsWith("aliyuncs.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQwenDotModel(string? modelId)
        {
            var modelName = GetUnqualifiedModelName(modelId);
            return modelName.StartsWith("qwen3.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQwen38Model(string? modelId)
        {
            var modelName = GetUnqualifiedModelName(modelId);
            return modelName.StartsWith("qwen3.8", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQwen3VlModel(string? modelId)
        {
            var modelName = GetUnqualifiedModelName(modelId);
            return modelName.StartsWith("qwen3-vl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SupportsPreserveThinking(string? modelId)
        {
            var modelName = GetUnqualifiedModelName(modelId);
            return modelName.StartsWith("qwen3.6", StringComparison.OrdinalIgnoreCase) ||
                   modelName.StartsWith("qwen3.8", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetUnqualifiedModelName(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return string.Empty;
            }

            var separatorIndex = Math.Max(modelId.LastIndexOf('/'), modelId.LastIndexOf('\\'));
            return modelId[(separatorIndex + 1)..];
        }

        private static string? GetQwen38ReasoningEffort(VllmChatOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ReasoningEffort))
            {
                return null;
            }

            var effort = options.ReasoningEffort.Trim().ToLowerInvariant();
            return effort is "low" or "medium" or "xhigh"
                ? effort
                : throw new ArgumentException(
                    "Qwen3.8 reasoning_effort must be one of: low, medium, xhigh.",
                    nameof(options));
        }

        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);

            if (options is VllmChatOptions vllmOptions)
            {
                var modelId = options?.ModelId ?? Metadata.DefaultModelId;
                if (IsQwenDotModel(modelId))
                {
                    var isQwen38 = IsQwen38Model(modelId);
                    var supportsPreserveThinking = SupportsPreserveThinking(modelId);
                    var reasoningEffort = isQwen38 ? GetQwen38ReasoningEffort(vllmOptions) : null;
                    request.ReasoningEffort = reasoningEffort;

                    if (UseAliyunThinkingParameter())
                    {
                        request.EnableThinking = vllmOptions.ThinkingEnabled;
                        request.PreserveThinking = supportsPreserveThinking ? vllmOptions.PreserveThinking : null;
                        request.ChatTemplateKwargs = null;
                    }
                    else
                    {
                        request.EnableThinking = null;
                        request.ChatTemplateKwargs = new Dictionary<string, object?>
                        {
                            ["enable_thinking"] = vllmOptions.ThinkingEnabled
                        };

                        if (supportsPreserveThinking && vllmOptions.PreserveThinking is bool preserveThinking)
                        {
                            request.ChatTemplateKwargs["preserve_thinking"] = preserveThinking;
                        }
                    }
                }
            }

            return request;
        }

        public VllmQwen3NextChatClient(string endpoint, string? token = null, string? modelId = "qwen3", HttpClient? httpClient = null, VllmApiMode apiMode = VllmApiMode.ChatCompletions)
            : base(NormalizeOpenAICompatibleEndpoint(endpoint, apiMode), token, modelId, httpClient, apiMode)
        {
        }

        protected override void ValidateMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            foreach (var message in messages)
            {
                foreach (var item in message.Contents)
                {
                    var mediaType = item switch
                    {
                        DataContent dataContent when dataContent.HasTopLevelMediaType("image") => "image",
                        DataContent dataContent when dataContent.HasTopLevelMediaType("video") => "video",
                        UriContent uriContent when uriContent.HasTopLevelMediaType("image") => "image",
                        UriContent uriContent when uriContent.HasTopLevelMediaType("video") => "video",
                        _ => null,
                    };

                    if (mediaType is null)
                    {
                        continue;
                    }

                    var modelId = options?.ModelId ?? Metadata.DefaultModelId;
                    var isSupported = mediaType == "video"
                        ? IsQwen38Model(modelId)
                        : IsQwenDotModel(modelId) || IsQwen3VlModel(modelId);

                    if (!isSupported)
                    {
                        throw new InvalidOperationException($"当前模型不支持{(mediaType == "video" ? "视频" : "图片")}输入");
                    }
                }
            }
        }

        private protected override IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            // Preserve native tool calls and their IDs for every downstream API adapter.
            if (content.Role == ChatRole.Tool)
            {
                foreach (var message in base.ToVllmChatRequestMessages(content))
                {
                    yield return message;
                }
                yield break;
            }

            var nativeMessage = base.ToVllmChatRequestMessages(content).Single();
            var text = string.Empty;
            var mediaParts = new List<JsonElement>();

            foreach (var item in content.Contents)
            {
                switch (item)
                {
                    case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                        {
                            mediaParts.Add(JsonSerializer.SerializeToElement(
                                new VllmOpenAIImageContentPart
                                {
                                    ImageUrl = new VllmOpenAIImageUrl
                                    {
                                        Url = dataContent.Uri.ToString(),
                                    }
                                },
                                typeof(VllmOpenAIImageContentPart),
                                JsonContext.Default));
                            break;
                        }

                    case DataContent dataContent when dataContent.HasTopLevelMediaType("video"):
                        {
                            mediaParts.Add(JsonSerializer.SerializeToElement(
                                new VllmOpenAIVideoContentPart
                                {
                                    VideoUrl = new VllmOpenAIImageUrl
                                    {
                                        Url = dataContent.Uri.ToString(),
                                    }
                                },
                                typeof(VllmOpenAIVideoContentPart),
                                JsonContext.Default));
                            break;
                        }

                    case UriContent uriContent when uriContent.HasTopLevelMediaType("image"):
                        {
                            mediaParts.Add(JsonSerializer.SerializeToElement(
                                new VllmOpenAIImageContentPart
                                {
                                    ImageUrl = new VllmOpenAIImageUrl
                                    {
                                        Url = uriContent.Uri.ToString(),
                                    }
                                },
                                typeof(VllmOpenAIImageContentPart),
                                JsonContext.Default));
                            break;
                        }

                    case UriContent uriContent when uriContent.HasTopLevelMediaType("video"):
                        {
                            mediaParts.Add(JsonSerializer.SerializeToElement(
                                new VllmOpenAIVideoContentPart
                                {
                                    VideoUrl = new VllmOpenAIImageUrl
                                    {
                                        Url = uriContent.Uri.ToString(),
                                    }
                                },
                                typeof(VllmOpenAIVideoContentPart),
                                JsonContext.Default));
                            break;
                        }

                    case TextContent textContent:
                        text += textContent.Text;
                        break;
                }
            }

            if (mediaParts.Count > 0)
            {
                var parts = new List<JsonElement>(capacity: mediaParts.Count + 1);
                parts.AddRange(mediaParts);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(JsonSerializer.SerializeToElement(
                        new VllmOpenAITextContentPart
                        {
                            Text = text
                        },
                        typeof(VllmOpenAITextContentPart),
                        JsonContext.Default));
                }

                nativeMessage.Content = JsonSerializer.Serialize(parts.ToArray(), typeof(JsonElement[]), JsonContext.Default);
                nativeMessage.Images = null;
                yield return nativeMessage;
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(text) || nativeMessage.ToolCalls is { Length: > 0 })
            {
                yield return nativeMessage;
            }
        }
    }
}
