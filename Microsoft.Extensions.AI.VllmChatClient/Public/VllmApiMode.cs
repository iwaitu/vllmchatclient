namespace Microsoft.Extensions.AI;

/// <summary>
/// Selects the vLLM OpenAI-compatible API surface used by <see cref="VllmBaseChatClient"/>.
/// </summary>
public enum VllmApiMode
{
    /// <summary>
    /// Use the OpenAI-compatible Chat Completions endpoint: /v1/chat/completions.
    /// </summary>
    ChatCompletions = 0,

    /// <summary>
    /// Use the OpenAI-compatible Responses endpoint: /v1/responses.
    /// </summary>
    Responses = 1,

    /// <summary>
    /// Use the Anthropic-compatible Messages endpoint: /v1/messages.
    /// </summary>
    AnthropicMessages = 2,
}
