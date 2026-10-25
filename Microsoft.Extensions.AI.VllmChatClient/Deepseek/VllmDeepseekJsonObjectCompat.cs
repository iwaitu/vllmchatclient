// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// deepseek-v4.1-flash-1017 supports JSON Object but not JSON Schema structured output. This helper holds the pure,
    /// stateless decisions of the transparent mapping used by <see cref="VllmDeepseekV4ChatClient"/>: which requests are mapped,
    /// whether the schema root can be an object, the single SDK system instruction, and the fixed response metadata keys.
    /// Nothing here keeps per-request state, so concurrent calls cannot affect each other.
    /// </summary>
    internal static class VllmDeepseekJsonObjectCompat
    {
        internal const string TargetModelId = "deepseek-v4.1-flash-1017";

        /// <summary>Stable marker on the first line of the SDK system instruction; it appears exactly once in each mapped request.</summary>
        internal const string InstructionMarker = "VllmDeepSeekJsonObjectSchema";

        internal const string RequestedKey = "vllm.structured_output.requested";
        internal const string ActualKey = "vllm.structured_output.actual";
        internal const string ProviderSchemaEnforcedKey = "vllm.structured_output.provider_schema_enforced";

        private static readonly string[] ConflictingExtensionKeys =
        [
            "format",
            "response_format",
            "structured_outputs",
            "output_config",
            "output_format",
            "text",
        ];

        /// <summary>True when the effective model is exactly the target snapshot and the caller asked for JSON (with or without a schema).</summary>
        internal static bool IsApplicable(ChatOptions? options, string? defaultModelId)
        {
            return options?.ResponseFormat is ChatResponseFormatJson
                && string.Equals(options.ModelId ?? defaultModelId, TargetModelId, StringComparison.Ordinal);
        }

        /// <summary>
        /// The caller-visible response format: <c>json_schema</c> when a schema was supplied, otherwise <c>json_object</c>.
        /// Only looks at whether the nullable <see cref="ChatResponseFormatJson.Schema"/> has a value; it never touches the JsonElement,
        /// so callers take this scalar snapshot before any network await and need no schema after that.
        /// </summary>
        internal static string RequestedKind(ChatResponseFormatJson format)
        {
            return format.Schema is null ? "json_object" : "json_schema";
        }

        /// <summary>Adds the three fixed scalar metadata keys; neither the schema nor any JsonElement is used here.</summary>
        internal static void StampMetadata(AdditionalPropertiesDictionary properties, string requestedKind)
        {
            properties[RequestedKey] = requestedKind;
            properties[ActualKey] = "json_object";
            properties[ProviderSchemaEnforcedKey] = false;
        }

        /// <summary>
        /// Fails before any HTTP request when the schema root is known to be unable to produce a JSON object.
        /// This is not schema evaluation: shapes that cannot be decided statically (anyOf/oneOf/allOf/$ref, missing type, ...) are allowed.
        /// Messages are fixed text and never contain schema content.
        /// </summary>
        internal static void ValidateRoot(ChatResponseFormatJson format)
        {
            // 没有提供 Schema（可空属性为 null）才是“无 Schema”；调用方提供了 Null/Undefined 的 JsonElement 属于无效 Schema，下面按非对象非布尔拒绝。
            if (format.Schema is not { } schema)
            {
                return;
            }

            switch (schema.ValueKind)
            {
                case JsonValueKind.True:
                    return;
                case JsonValueKind.False:
                    throw new NotSupportedException(
                        "DeepSeek JSON Object compatibility cannot satisfy a JSON schema that accepts no value; no request was sent.");
                case JsonValueKind.Object:
                    break;
                default:
                    throw new ArgumentException(
                        "The response format schema must be a JSON object or a boolean schema; no request was sent.");
            }

            if (schema.TryGetProperty("type", out var type))
            {
                if (type.ValueKind == JsonValueKind.String && !type.ValueEquals("object"))
                {
                    throw RootNotObject();
                }

                if (type.ValueKind == JsonValueKind.Array && !ArrayHas(type, static item => item.ValueKind == JsonValueKind.String && item.ValueEquals("object")))
                {
                    throw RootNotObject();
                }
            }

            if (schema.TryGetProperty("const", out var constant) && constant.ValueKind != JsonValueKind.Object)
            {
                throw RootNotObject();
            }

            if (schema.TryGetProperty("enum", out var values) &&
                values.ValueKind == JsonValueKind.Array &&
                !ArrayHas(values, static item => item.ValueKind == JsonValueKind.Object))
            {
                throw RootNotObject();
            }
        }

        private static bool ArrayHas(JsonElement array, Func<JsonElement, bool> predicate)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (predicate(item))
                {
                    return true;
                }
            }

            return false;
        }

        private static NotSupportedException RootNotObject()
        {
            return new NotSupportedException(
                "DeepSeek JSON Object compatibility requires a response format schema whose root can be a JSON object; no request was sent.");
        }

        /// <summary>
        /// True for extension keys that could override or duplicate the response format. A mapped request must not carry them, so the
        /// legacy schema fields cannot be sent again alongside <c>json_object</c>.
        /// </summary>
        internal static bool IsConflictingExtensionKey(string key)
        {
            foreach (var name in ConflictingExtensionKeys)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rejects reserved format keys in the caller's <see cref="ChatOptions.AdditionalProperties"/> for a mapped request, before any HTTP.
        /// Other keys are left alone and the caller's dictionary is only read, never modified.
        /// </summary>
        internal static void RejectReservedAdditionalProperties(ChatOptions options)
        {
            if (options.AdditionalProperties is not { } properties)
            {
                return;
            }

            foreach (var key in properties.Keys)
            {
                if (IsConflictingExtensionKey(key))
                {
                    throw ExtensionConflict();
                }
            }
        }

        internal static NotSupportedException ExtensionConflict()
        {
            return new NotSupportedException(
                "The request carries an extension field that overrides the response format, which DeepSeek JSON Object compatibility cannot combine; no request was sent.");
        }

        /// <summary>
        /// The single SDK system instruction: marker line, a fixed JSON-object directive, then (when a schema was supplied) the original schema text
        /// on its own final line. The schema text is copied into a string here, so it does not depend on the caller's JsonElement afterwards.
        /// </summary>
        internal static string BuildInstruction(ChatResponseFormatJson format)
        {
            var text = new System.Text.StringBuilder();
            text.Append(InstructionMarker).Append('\n');
            if (format.Schema is { } schema)
            {
                text.Append("Respond with exactly one valid JSON object and nothing else (no markdown fences, no commentary). ")
                    .Append("The server does not enforce a schema, so the object must follow this JSON Schema exactly, including field names, types, required fields and enum values:\n")
                    .Append(schema.GetRawText());
            }
            else
            {
                text.Append("Respond with exactly one valid JSON object and nothing else (no markdown fences, no commentary).");
            }

            return text.ToString();
        }
    }
}
