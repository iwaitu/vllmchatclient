using System.Text.Json;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Strict parsing of provider usage counters. A counter is a non-negative integer that fits in <see cref="long"/>; a missing or JSON-null counter is
/// "unknown" (<c>null</c>), never zero. Anything else (negative, fractional, too large, not a number, or a sum that overflows) is a protocol error that
/// always surfaces as <see cref="InvalidOperationException"/> with the fixed text <see cref="InvalidMessage"/>; it never contains the offending value or any
/// response content.
/// </summary>
internal static class VllmUsageCounters
{
    internal const string InvalidMessage = "Invalid provider usage counters.";

    internal static InvalidOperationException Invalid() => new(InvalidMessage);

    /// <summary>Reads one counter. Absent / null / undefined → <c>null</c>; invalid → <see cref="InvalidOperationException"/>.</summary>
    internal static long? Read(JsonElement? value)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var number) || number < 0)
        {
            throw Invalid();
        }

        return number;
    }

    internal static long? ReadProperty(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var value) ? Read(value) : null;
    }

    internal static long Add(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    /// <summary>
    /// Lenient int view used only by the legacy DTO accessors that other clients still read (<c>Usage.PromptTokens</c> and friends):
    /// unknown or unusable → 0, larger than <see cref="int.MaxValue"/> → <see cref="int.MaxValue"/>. The strict path above is what the base client uses.
    /// </summary>
    internal static int ToIntOrZero(JsonElement? value)
    {
        if (value is { ValueKind: JsonValueKind.Number } element && element.TryGetInt64(out var number) && number > 0)
        {
            return number > int.MaxValue ? int.MaxValue : (int)number;
        }

        return 0;
    }
}

/// <summary>
/// One Anthropic Messages response's usage. Every field keeps its latest cumulative value (never added across events), so a delta that repeats the start's
/// input numbers cannot double count and a partial update never clears an earlier field. Raw <c>input_tokens</c> excludes cached tokens, so the standard
/// <see cref="UsageDetails.InputTokenCount"/> is uncached + cache read + cache creation. Rules for missing fields:
/// <list type="bullet">
/// <item>Input is known only when <c>input_tokens</c> is present; cache fields that are absent add nothing and create no <see cref="UsageDetails.AdditionalCounts"/> key.</item>
/// <item>Cache creation uses <c>cache_creation_input_tokens</c> when present, otherwise the sum of whichever 5m / 1h parts are present (never both).</item>
/// <item>Output is known only when <c>output_tokens</c> is present; Total only when both Input and Output are known. Explicit zero is kept.</item>
/// </list>
/// </summary>
internal sealed class VllmAnthropicUsageAccumulator
{
    private long? _input;
    private long? _output;
    private long? _cacheRead;
    private long? _cacheCreation;
    private long? _creation5m;
    private long? _creation1h;

    public bool HasAny => _input.HasValue || _output.HasValue || _cacheRead.HasValue || _cacheCreation.HasValue || _creation5m.HasValue || _creation1h.HasValue;

    /// <summary>Merges a raw <c>usage</c> JSON object (Sonnet 5.5 path). Null / undefined is ignored; any other non-object is invalid.</summary>
    public void Merge(JsonElement usage)
    {
        if (usage.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (usage.ValueKind != JsonValueKind.Object)
        {
            throw VllmUsageCounters.Invalid();
        }

        Take(ref _input, VllmUsageCounters.ReadProperty(usage, "input_tokens"));
        Take(ref _output, VllmUsageCounters.ReadProperty(usage, "output_tokens"));
        Take(ref _cacheRead, VllmUsageCounters.ReadProperty(usage, "cache_read_input_tokens"));
        Take(ref _cacheCreation, VllmUsageCounters.ReadProperty(usage, "cache_creation_input_tokens"));

        if (usage.TryGetProperty("cache_creation", out var creation) && creation.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (creation.ValueKind != JsonValueKind.Object)
            {
                throw VllmUsageCounters.Invalid();
            }

            Take(ref _creation5m, VllmUsageCounters.ReadProperty(creation, "ephemeral_5m_input_tokens"));
            Take(ref _creation1h, VllmUsageCounters.ReadProperty(creation, "ephemeral_1h_input_tokens"));
        }
    }

    /// <summary>Merges the typed <c>usage</c> DTO (generic Anthropic path).</summary>
    public void Merge(VllmAnthropicUsage? usage)
    {
        if (usage is null)
        {
            return;
        }

        Take(ref _input, VllmUsageCounters.Read(usage.InputTokens));
        Take(ref _output, VllmUsageCounters.Read(usage.OutputTokens));
        Take(ref _cacheRead, VllmUsageCounters.Read(usage.CacheReadInputTokens));
        Take(ref _cacheCreation, VllmUsageCounters.Read(usage.CacheCreationInputTokens));
        if (usage.CacheCreation is { } creation)
        {
            Take(ref _creation5m, VllmUsageCounters.Read(creation.Ephemeral5mInputTokens));
            Take(ref _creation1h, VllmUsageCounters.Read(creation.Ephemeral1hInputTokens));
        }
    }

    public UsageDetails? ToUsageDetails()
    {
        if (!HasAny)
        {
            return null;
        }

        long? creation = _cacheCreation;
        if (!creation.HasValue && (_creation5m.HasValue || _creation1h.HasValue))
        {
            creation = VllmUsageCounters.Add(_creation5m ?? 0, _creation1h ?? 0);
        }

        long? input = null;
        if (_input.HasValue)
        {
            input = VllmUsageCounters.Add(VllmUsageCounters.Add(_input.Value, _cacheRead ?? 0), creation ?? 0);
        }

        long? total = null;
        if (input.HasValue && _output.HasValue)
        {
            total = VllmUsageCounters.Add(input.Value, _output.Value);
        }

        var details = new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = _output,
            TotalTokenCount = total,
            AdditionalCounts = new(),
        };

        // Only fields the provider actually returned; a total derived from the TTL parts is not published as "cache_creation_input_tokens".
        Add(details, "input_tokens", _input);
        Add(details, "output_tokens", _output);
        Add(details, "cache_read_input_tokens", _cacheRead);
        Add(details, "cache_creation_input_tokens", _cacheCreation);
        Add(details, "cache_creation.ephemeral_5m_input_tokens", _creation5m);
        Add(details, "cache_creation.ephemeral_1h_input_tokens", _creation1h);
        return details;
    }

    private static void Take(ref long? target, long? value)
    {
        if (value.HasValue)
        {
            target = value;
        }
    }

    private static void Add(UsageDetails details, string key, long? value)
    {
        if (value.HasValue)
        {
            details.AdditionalCounts![key] = value.Value;
        }
    }
}

/// <summary>
/// One Chat Completions or Responses response's usage. <c>prompt_tokens</c> / <c>input_tokens</c> is already the provider's total input (cache included), so the
/// cached part is only exposed in <see cref="UsageDetails.AdditionalCounts"/> and is never added again. Missing counters stay <c>null</c>; the provider's
/// <c>total_tokens</c> is kept when it is known, otherwise Total is derived only when both Input and Output are known.
/// </summary>
internal sealed class VllmCompletionUsageAccumulator(string nativeCachedKey)
{
    private long? _input;
    private long? _output;
    private long? _total;
    private long? _cached;

    public bool HasAny => _input.HasValue || _output.HasValue || _total.HasValue || _cached.HasValue;

    public void Merge(Usage? usage)
    {
        if (usage is null)
        {
            return;
        }

        Take(ref _input, VllmUsageCounters.Read(usage.RawPromptTokens));
        Take(ref _output, VllmUsageCounters.Read(usage.RawCompletionTokens));
        Take(ref _total, VllmUsageCounters.Read(usage.RawTotalTokens));
        Take(ref _cached, VllmUsageCounters.Read(usage.PromptTokensDetails?.CachedTokens));
    }

    public void Merge(VllmResponsesUsage? usage)
    {
        if (usage is null)
        {
            return;
        }

        Take(ref _input, VllmUsageCounters.Read(usage.InputTokens));
        Take(ref _output, VllmUsageCounters.Read(usage.OutputTokens));
        Take(ref _total, VllmUsageCounters.Read(usage.TotalTokens));
        Take(ref _cached, VllmUsageCounters.Read(usage.InputTokensDetails?.CachedTokens));
    }

    public UsageDetails? ToUsageDetails()
    {
        if (!HasAny)
        {
            return null;
        }

        long? total = _total;
        if (!total.HasValue && _input.HasValue && _output.HasValue)
        {
            total = VllmUsageCounters.Add(_input.Value, _output.Value);
        }

        var details = new UsageDetails
        {
            InputTokenCount = _input,
            OutputTokenCount = _output,
            TotalTokenCount = total,
        };

        if (_cached.HasValue)
        {
            // The native dotted name plus one documented alias; neither is part of Input/Total and the alias is the same single quantity, not a second consumption.
            details.AdditionalCounts = new()
            {
                [nativeCachedKey] = _cached.Value,
                ["cached_input_tokens"] = _cached.Value,
            };
        }

        return details;
    }

    private static void Take(ref long? target, long? value)
    {
        if (value.HasValue)
        {
            target = value;
        }
    }
}
