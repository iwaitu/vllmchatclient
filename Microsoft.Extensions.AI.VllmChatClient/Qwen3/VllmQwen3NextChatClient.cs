using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    public class VllmQwen3NextChatClient : VllmBaseChatClient
    {
        private protected override VllmOpenAIChatRequest ToVllmChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
        {
            var request = base.ToVllmChatRequest(messages, options, stream);
            request.ToolChoice = null;
            return request;
        }

        public VllmQwen3NextChatClient(string endpoint, string? token = null, string? modelId = "qwen3", HttpClient? httpClient = null)
            : base(endpoint, token, modelId, httpClient)
        {
        }

        protected override void ValidateMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            foreach (var message in messages)
            {
                foreach (var item in message.Contents)
                {
                    if (item is DataContent dataContent && dataContent.HasTopLevelMediaType("image"))
                    {
                        var modelId = options?.ModelId ?? Metadata.DefaultModelId;
                        if (string.IsNullOrWhiteSpace(modelId) ||
                            !modelId.StartsWith("qwen3-vl", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("当前模型不支持多模态");
                        }
                    }
                }
            }
        }

        private protected override IEnumerable<VllmOpenAIChatRequestMessage> ToVllmChatRequestMessages(ChatMessage content)
        {
            var text = string.Empty;
            var imageParts = new List<object>();

            foreach (var item in content.Contents)
            {
                switch (item)
                {
                    case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                        {
                            var base64 = Convert.ToBase64String(dataContent.Data
#if NET
                                .Span);
#else
                                .ToArray());
#endif
                            var mime = string.IsNullOrWhiteSpace(dataContent.MediaType) ? "image/jpeg" : dataContent.MediaType;
                            imageParts.Add(new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{mime};base64,{base64}",
                                }
                            });
                            break;
                        }

                    case TextContent textContent:
                        text = textContent.Text;
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

            if (imageParts.Count > 0)
            {
                var parts = new List<object>(capacity: imageParts.Count + 1);
                parts.AddRange(imageParts);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(new { type = "text", text });
                }

                yield return new VllmOpenAIChatRequestMessage
                {
                    Role = content.Role.Value,
                    Content = JsonSerializer.Serialize(parts, ToolCallJsonSerializerOptions),
                };
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new VllmOpenAIChatRequestMessage
                {
                    Role = content.Role.Value,
                    Content = text,
                };
            }
        }
    }
}
