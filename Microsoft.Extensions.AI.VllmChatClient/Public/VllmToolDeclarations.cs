namespace Microsoft.Extensions.AI;

/// <summary>
/// Explicit opt-in to keep sending the tool definitions when <see cref="ChatOptions.ToolMode"/> is <see cref="ChatToolMode.None"/>.
/// By default such a request carries <c>tool_choice: "none"</c> and no <c>tools</c>. Providers render the tool definitions ahead of
/// the conversation, so a turn that drops them no longer shares a prefix with the turns before it and misses the provider's prompt cache.
/// Set the key to keep the definitions on the wire while the model is still told not to call them:
/// <code>
/// var options = new ChatOptions { Tools = tools, ToolMode = ChatToolMode.None };
/// options.AdditionalProperties = new AdditionalPropertiesDictionary
/// {
///     [VllmToolDeclarations.KeepWhenToolModeNoneKey] = true,
/// };
/// </code>
/// A missing key, <c>null</c> or <c>false</c> keeps the request exactly as before; any other value is rejected before a request is sent.
/// The key itself is never sent. It applies to Chat Completions, Responses and Anthropic Messages requests built by
/// <see cref="VllmBaseChatClient"/>; clients with their own tool builders (Qwq, Gemma, Gemini 3) are unchanged. Whether the backend
/// honours <c>tool_choice: "none"</c> with tools present is the caller's decision, and tool calls written as text are still not parsed.
/// </summary>
public static class VllmToolDeclarations
{
    /// <summary>The <see cref="ChatOptions.AdditionalProperties"/> key that carries this setting.</summary>
    public const string KeepWhenToolModeNoneKey = "tools.keep_when_none";

    internal static bool KeepWhenToolModeNone(ChatOptions? options)
    {
        if (options?.AdditionalProperties is not { } properties ||
            !properties.TryGetValue(KeepWhenToolModeNoneKey, out var value) ||
            value is null)
        {
            return false;
        }

        return value switch
        {
            bool flag => flag,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True } => true,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.False } => false,
            _ => throw new ArgumentException(
                "ChatOptions.AdditionalProperties[\"tools.keep_when_none\"] must be null or a boolean; no request was sent.",
                nameof(options)),
        };
    }
}
