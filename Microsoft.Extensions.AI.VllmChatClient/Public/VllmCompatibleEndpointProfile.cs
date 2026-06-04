namespace Microsoft.Extensions.AI;

/// <summary>
/// Describes provider-specific request defaults for OpenAI-compatible, Responses, or Anthropic Messages endpoints.
/// </summary>
public sealed class VllmCompatibleEndpointProfile
{
    /// <summary>
    /// Optional provider name exposed through diagnostics and retry logs.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Header used when a token is supplied. Defaults to Authorization: Bearer for non-Anthropic modes.
    /// </summary>
    public string? TokenHeaderName { get; init; }

    /// <summary>
    /// Authentication scheme used when <see cref="TokenHeaderName"/> is Authorization.
    /// </summary>
    public string? TokenHeaderScheme { get; init; } = "Bearer";

    /// <summary>
    /// Additional headers sent on every request.
    /// </summary>
    public IReadOnlyDictionary<string, string>? DefaultHeaders { get; init; }

    /// <summary>
    /// Default provider extension values. Chat Completions serializes these under extra_body;
    /// Responses serializes them as extension fields.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExtraBody { get; init; }

    /// <summary>
    /// Maps <see cref="VllmChatOptions.ThinkingEnabled"/> into a provider-specific request field.
    /// </summary>
    public VllmCompatibleThinkingParameter ThinkingParameter { get; init; }

    /// <summary>
    /// String value used by thinking modes that serialize an enabled/disabled string.
    /// </summary>
    public string ThinkingEnabledValue { get; init; } = "enabled";

    /// <summary>
    /// String value used by thinking modes that serialize an enabled/disabled string.
    /// </summary>
    public string ThinkingDisabledValue { get; init; } = "disabled";

    /// <summary>
    /// Reasoning effort used by <see cref="VllmCompatibleThinkingParameter.ReasoningEffort"/>.
    /// </summary>
    public string ReasoningEffort { get; init; } = "high";

    /// <summary>
    /// Moves ChatOptions temperature, top_p, and max output tokens to top-level OpenAI-compatible fields.
    /// </summary>
    public bool UseTopLevelGenerationOptions { get; init; }

    /// <summary>
    /// Uses max_completion_tokens instead of max_tokens when <see cref="UseTopLevelGenerationOptions"/> is enabled.
    /// </summary>
    public bool UseMaxCompletionTokens { get; init; }

    /// <summary>
    /// Default profile for OpenAI-compatible endpoints that use Authorization: Bearer.
    /// </summary>
    public static VllmCompatibleEndpointProfile OpenAICompatible { get; } = new();

    /// <summary>
    /// Default profile for Anthropic Messages-compatible endpoints.
    /// </summary>
    public static VllmCompatibleEndpointProfile AnthropicMessages { get; } = new()
    {
        TokenHeaderName = "x-api-key",
        DefaultHeaders = new Dictionary<string, string>
        {
            ["anthropic-version"] = "2023-06-01"
        }
    };
}

/// <summary>
/// Provider-specific request field used for the generic thinking toggle.
/// </summary>
public enum VllmCompatibleThinkingParameter
{
    /// <summary>
    /// Do not serialize a profile-driven thinking field.
    /// </summary>
    None = 0,

    /// <summary>
    /// Serialize thinking: { type: "enabled" | "disabled" }.
    /// </summary>
    TopLevelThinkingType = 1,

    /// <summary>
    /// Serialize enable_thinking: true | false.
    /// </summary>
    TopLevelEnableThinking = 2,

    /// <summary>
    /// Serialize chat_template_kwargs: { enable_thinking: true | false }.
    /// </summary>
    ChatTemplateEnableThinking = 3,

    /// <summary>
    /// Serialize extra_body: { thinking: { type: "enabled" | "disabled" } }.
    /// </summary>
    ExtraBodyThinkingType = 4,

    /// <summary>
    /// Serialize reasoning: { enabled: true | false }.
    /// </summary>
    ReasoningEnabled = 5,

    /// <summary>
    /// Serialize reasoning: { effort: "..." } when thinking is enabled.
    /// </summary>
    ReasoningEffort = 6,
}
