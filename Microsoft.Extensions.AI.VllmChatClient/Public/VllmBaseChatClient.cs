using Microsoft.Shared.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Extensions.AI
{
    public abstract class VllmBaseChatClient : IChatClient
    {
        private static readonly JsonElement _schemalessJsonResponseFormatValue = JsonDocument.Parse("\"json\"").RootElement;
        private static readonly ConcurrentDictionary<string, (string? instruction, DateTime timestamp)> _skillInstructionCache = new(StringComparer.OrdinalIgnoreCase);
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

            if (string.IsNullOrEmpty(reason))
            {
                reason = responseMessage?.Reasoning ?? string.Empty;
            }

            if (string.IsNullOrEmpty(reason) && responseMessage?.ReasoningDetails?.FirstOrDefault(x => x.Type == "reasoning.text") is { } detail)
            {
                reason = detail.Text;
            }

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

                    var reasoningUpdate = HandleStreamingReasoningContent(message, responseId, chunk.Model ?? Metadata.DefaultModelId);
                    if (reasoningUpdate != null)
                    {
                        yield return reasoningUpdate;
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

        private protected virtual IEnumerable<ChatMessage> PrepareMessagesWithSkills(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            if (options is not VllmChatOptions vllmOptions)
            {
                return messages;
            }

            if (!vllmOptions.EnableSkills && string.IsNullOrWhiteSpace(vllmOptions.SkillDirectoryPath))
            {
                return messages;
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

            var skillInstruction = LoadSkillInstruction(vllmOptions.SkillDirectoryPath);
            if (string.IsNullOrWhiteSpace(skillInstruction))
            {
                return messages;
            }

            return [new ChatMessage(ChatRole.System, skillInstruction), .. messages];
        }

        private static string? LoadSkillInstruction(string? skillDirectoryPath)
        {
            var effectiveDirectory = string.IsNullOrWhiteSpace(skillDirectoryPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "skills")
                : Path.GetFullPath(skillDirectoryPath);

            try
            {
                if (!Directory.Exists(effectiveDirectory))
                {
                    return null; // 不缓存，下次继续检查
                }

                var dirInfo = new DirectoryInfo(effectiveDirectory);
                var lastWrite = dirInfo.LastWriteTimeUtc;

                // 检查缓存是否有效
                if (_skillInstructionCache.TryGetValue(effectiveDirectory, out var cached)
                    && cached.timestamp >= lastWrite
                    && cached.instruction != null)
                {
                    return cached.instruction;
                }

                // 扫描顶层 *.md 文件
                var topLevelSkills = Directory
                    .EnumerateFiles(effectiveDirectory, "*.md", SearchOption.TopDirectoryOnly)
                    .Select(file => (title: Path.GetFileNameWithoutExtension(file), path: file))
                    .ToList();

                // 扫描子目录中的 SKILL.md 文件（例如 ./skills/test/SKILL.md）
                var subDirSkills = Directory
                    .EnumerateDirectories(effectiveDirectory)
                    .Select(dir =>
                    {
                        var skillMd = Path.Combine(dir, "SKILL.md");
                        if (File.Exists(skillMd))
                        {
                            return (title: Path.GetFileName(dir), path: skillMd);
                        }
                        // 也支持小写 skill.md
                        var skillMdLower = Path.Combine(dir, "skill.md");
                        if (File.Exists(skillMdLower))
                        {
                            return (title: Path.GetFileName(dir), path: skillMdLower);
                        }
                        return (title: (string?)null, path: (string?)null);
                    })
                    .Where(x => x.title != null)
                    .Select(x => (title: x.title!, path: x.path!))
                    .ToList();

                var allSkillFiles = topLevelSkills
                    .Concat(subDirSkills)
                    .OrderBy(x => x.title, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (allSkillFiles.Length == 0)
                {
                    return null; // 不缓存空结果
                }

                var sections = allSkillFiles
                    .Select(skill =>
                    {
                        try
                        {
                            var content = File.ReadAllText(skill.path).Trim();
                            return string.IsNullOrWhiteSpace(content)
                                ? null
                                : $"## Skill: {skill.title}\n{content}";
                        }
                        catch (Exception)
                        {
                            return null; // 单个文件失败不影响其他
                        }
                    })
                    .Where(section => section is not null)
                    .Cast<string>()
                    .ToArray();

                if (sections.Length == 0)
                {
                    return null;
                }

                var instruction = $"""
                    # Skills
                    
                    You have {sections.Length} skill(s) loaded from the local skills directory.
                    Based on the user's question, select the most relevant skill(s) and follow the instructions defined in them.
                    If no skill is relevant, answer the question directly without referencing any skill.
                    If the user's question relates to multiple skills, combine the instructions from all applicable skills.
                    
                    You also have two built-in tools:
                    - **ListSkillFiles**: Lists all available skill files (.md) in the skills directory.
                    - **ReadSkillFile**: Reads the full content of a specific skill file by filename.
                    Use these tools when the user asks to browse, inspect, or discover available skills.
                    
                    {string.Join("\n\n", sections)}
                    """;

                // 仅缓存有效结果
                _skillInstructionCache[effectiveDirectory] = (instruction, lastWrite);
                return instruction;
            }
            catch (Exception)
            {
                // 任何异常都静默处理，视为 skill 加载失败
                return null;
            }
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

                    var results = new List<string>();

                    // 顶层 *.md 文件
                    var topLevelFiles = Directory.EnumerateFiles(skillsDir, "*.md", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(f => f != null)
                        .Cast<string>();
                    results.AddRange(topLevelFiles);

                    // 子目录中的 SKILL.md
                    foreach (var dir in Directory.EnumerateDirectories(skillsDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        var skillMd = Path.Combine(dir, "SKILL.md");
                        var skillMdLower = Path.Combine(dir, "skill.md");
                        if (File.Exists(skillMd))
                        {
                            results.Add($"{dirName}/ (SKILL.md)");
                        }
                        else if (File.Exists(skillMdLower))
                        {
                            results.Add($"{dirName}/ (skill.md)");
                        }
                    }

                    return results.Count > 0
                        ? string.Join("\n", results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        : "No skill files found.";
                },
                "ListSkillFiles",
                "List all available skill files (.md) in the skills directory, including subdirectory skills.");

            yield return AIFunctionFactory.Create(
                [Description("Read the full content of a specific skill file. For top-level files, use filename directly. For subdirectory skills, use 'dirname/SKILL.md' format.")]
                (string fileName) =>
                {
                    var filePath = Path.Combine(skillsDir, fileName);
                    if (!File.Exists(filePath))
                    {
                        // 尝试子目录中的 SKILL.md
                        var subDirSkill = Path.Combine(skillsDir, fileName, "SKILL.md");
                        if (File.Exists(subDirSkill))
                        {
                            return File.ReadAllText(subDirSkill);
                        }
                        var subDirSkillLower = Path.Combine(skillsDir, fileName, "skill.md");
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
        }
    }
}
