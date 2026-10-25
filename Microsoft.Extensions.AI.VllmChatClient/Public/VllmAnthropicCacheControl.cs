namespace Microsoft.Extensions.AI;

/// <summary>
/// Explicit opt-in to Anthropic automatic prompt caching for the native Anthropic Messages mode (<see cref="VllmApiMode.AnthropicMessages"/>).
/// The SDK never enables caching on its own. Put an instance in <see cref="ChatOptions.AdditionalProperties"/> under <see cref="OptionKey"/>:
/// <code>
/// var options = new ChatOptions();
/// options.AdditionalProperties = new AdditionalPropertiesDictionary
/// {
///     [VllmAnthropicCacheControl.OptionKey] = new VllmAnthropicCacheControl("5m"),
/// };
/// </code>
/// The request then carries a top-level <c>cache_control</c> object (<c>{"type":"ephemeral"}</c>, plus <c>"ttl"</c> when set). A <c>null</c> value switches it off,
/// a missing key keeps the request exactly as before. The instance is immutable and safe to share between concurrent calls. Any other value type is rejected
/// before a request is sent, and so is a non-null value used with a non-Anthropic API mode (it is never silently dropped). Whether an Anthropic-compatible
/// third-party gateway accepts the field is the caller's decision; the SDK does not probe, retry or switch protocol.
/// </summary>
public sealed class VllmAnthropicCacheControl
{
    /// <summary>The <see cref="ChatOptions.AdditionalProperties"/> key that carries this setting.</summary>
    public const string OptionKey = "anthropic.cache_control";

    /// <summary>Creates the setting. <paramref name="ttl"/> must be <c>null</c> (provider default), <c>"5m"</c> or <c>"1h"</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="ttl"/> is any other value.</exception>
    public VllmAnthropicCacheControl(string? ttl = null)
    {
        if (ttl is not (null or "5m" or "1h"))
        {
            throw new ArgumentException("The cache control ttl must be null, \"5m\" or \"1h\".", nameof(ttl));
        }

        Ttl = ttl;
    }

    /// <summary>Always <c>ephemeral</c>.</summary>
    public string Type => "ephemeral";

    /// <summary>The cache lifetime: <c>null</c> (provider default), <c>"5m"</c> or <c>"1h"</c>.</summary>
    public string? Ttl { get; }
}
