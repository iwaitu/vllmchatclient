// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// Native Anthropic Messages support that is specific to the Claude Sonnet 5.5 family: the exact model-family boundary and the versioned opaque
    /// carrier for <c>thinking</c> / <c>redacted_thinking</c> blocks. Hand-written wire handling over <see cref="JsonDocument"/> / <see cref="Utf8JsonWriter"/>
    /// only (no reflection, no new source-generated types). Nothing in here is used for any other model.
    /// </summary>
    internal static class VllmClaudeSonnet55
    {
        /// <summary>Every opaque carrier of this SDK starts with this family prefix; the digits after it are the format version.</summary>
        internal const string OpaquePrefixFamily = "vllm.anthropic.block.v";

        /// <summary>Format version 1: the prefix followed by the complete original JSON text of exactly one content block.</summary>
        internal const string OpaquePrefix = OpaquePrefixFamily + "1:";

        private static readonly string[] FamilyStems = ["claude-sonnet-5-5", "claude-sonnet-5.5"];

        /// <summary>
        /// True for <c>claude-sonnet-5-5</c> / <c>claude-sonnet-5.5</c>, optionally behind a provider path (<c>anthropic/…</c>) and optionally followed by
        /// exactly an 8-digit date (<c>-20260101</c> or <c>@20260101</c>). <c>claude-sonnet-5-50</c>, <c>claude-sonnet-5.50</c>, <c>claude-sonnet-5-5x</c>,
        /// <c>claude-sonnet-5-5-preview</c> and other families do not match.
        /// </summary>
        internal static bool IsFamily(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            var id = modelId!.Trim().ToLowerInvariant();
            var slash = id.LastIndexOf('/');
            if (slash >= 0)
            {
                id = id.Substring(slash + 1);
            }

            foreach (var stem in FamilyStems)
            {
                if (!id.StartsWith(stem, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = id.Substring(stem.Length);
                if (rest.Length == 0)
                {
                    return true;
                }

                if ((rest[0] == '-' || rest[0] == '@') && rest.Length == 9)
                {
                    for (var i = 1; i < rest.Length; i++)
                    {
                        if (rest[i] < '0' || rest[i] > '9')
                        {
                            return false;
                        }
                    }

                    return true;
                }

                return false;
            }

            return false;
        }

        internal static bool IsOpaque(string? protectedData)
            => protectedData is not null && protectedData.StartsWith(OpaquePrefixFamily, StringComparison.Ordinal);

        internal static string? GetString(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        internal static int? GetInt(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : null;

        /// <summary>
        /// Wraps a validated <c>thinking</c> / <c>redacted_thinking</c> block as the standard MEAI opaque reasoning item:
        /// <see cref="TextReasoningContent.Text"/> is the empty string (never null), <see cref="TextReasoningContent.ProtectedData"/> is the versioned carrier.
        /// </summary>
        internal static TextReasoningContent CreateOpaqueItem(JsonElement block)
        {
            ValidateBlock(block, "response");
            return new TextReasoningContent(string.Empty) { ProtectedData = OpaquePrefix + block.GetRawText() };
        }

        /// <summary>
        /// Returns the original block JSON from a carrier. Unsupported version, malformed or truncated JSON and structurally invalid blocks are rejected
        /// with <see cref="NotSupportedException"/>; the carrier text is never echoed into the message.
        /// </summary>
        internal static JsonElement Decode(string protectedData)
        {
            if (!protectedData.StartsWith(OpaquePrefix, StringComparison.Ordinal))
            {
                throw new NotSupportedException("The opaque Anthropic reasoning block has an unsupported version or format and cannot be replayed.");
            }

            try
            {
                using var document = JsonDocument.Parse(protectedData.Substring(OpaquePrefix.Length));
                var block = document.RootElement.Clone();
                ValidateBlock(block, "history");
                return block;
            }
            catch (JsonException)
            {
                throw new NotSupportedException("The opaque Anthropic reasoning block is malformed or truncated and cannot be replayed.");
            }
        }

        private static void ValidateBlock(JsonElement block, string origin)
        {
            var type = GetString(block, "type");
            if (type == "thinking")
            {
                if (GetString(block, "thinking") is null || GetString(block, "signature") is null)
                {
                    throw new NotSupportedException($"An Anthropic thinking block in the {origin} lacks a string 'thinking' or 'signature' and cannot be carried or replayed.");
                }
            }
            else if (type == "redacted_thinking")
            {
                if (GetString(block, "data") is null)
                {
                    throw new NotSupportedException($"An Anthropic redacted_thinking block in the {origin} lacks a string 'data' and cannot be carried or replayed.");
                }
            }
            else
            {
                throw new NotSupportedException($"The opaque Anthropic reasoning carrier in the {origin} holds an unsupported block type.");
            }
        }

        /// <summary>
        /// Rebuilds the completed streamed thinking block from its <c>content_block_start</c> object: every start property (including unknown ones) is kept in
        /// its original order; <c>thinking</c> and <c>signature</c> take the accumulated values (and are appended when the start object lacked them).
        /// </summary>
        internal static JsonElement BuildThinkingBlock(JsonElement start, string thinking, string signature)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                var wroteThinking = false;
                var wroteSignature = false;
                writer.WriteStartObject();
                foreach (var property in start.EnumerateObject())
                {
                    if (property.NameEquals("thinking"))
                    {
                        writer.WriteString("thinking", thinking);
                        wroteThinking = true;
                    }
                    else if (property.NameEquals("signature"))
                    {
                        writer.WriteString("signature", signature);
                        wroteSignature = true;
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }

                if (!wroteThinking)
                {
                    writer.WriteString("thinking", thinking);
                }

                if (!wroteSignature)
                {
                    writer.WriteString("signature", signature);
                }

                writer.WriteEndObject();
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }

        // 用量合并已抽到 Public/VllmUsageSupport.cs 的 VllmAnthropicUsageAccumulator（Sonnet 与通用 Anthropic 路径共用）。

        /// <summary>State of one streamed content block, keyed by its stream <c>index</c>.</summary>
        internal sealed class StreamBlock
        {
            public StreamBlock(string type, JsonElement start)
            {
                Type = type;
                Start = start;
            }

            public string Type { get; }

            public JsonElement Start { get; }

            public StringBuilder Thinking { get; } = new();

            public StringBuilder Signature { get; } = new();

            public string? ToolId { get; set; }

            public string? ToolName { get; set; }

            public string ToolArguments { get; set; } = string.Empty;
        }
    }
}
