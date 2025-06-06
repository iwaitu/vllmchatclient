﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI
{
    internal static class VllmUtilities
    {
        public static HttpClient SharedClient { get; } = new()
        {
            // Expected use is localhost access for non-production use. Typical production use should supply
            // an HttpClient configured with whatever more robust resilience policy / handlers are appropriate.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        public static void TransferNanosecondsTime<TResponse>(TResponse response, Func<TResponse, long?> getNanoseconds, string key, ref AdditionalPropertiesDictionary<long>? metadata)
        {
            if (getNanoseconds(response) is long duration)
            {
                try
                {
                    (metadata ??= [])[key] = duration;
                }
                catch (OverflowException)
                {
                    // Ignore options that don't convert
                }
            }
        }

        [DoesNotReturn]
        public static async ValueTask ThrowUnsuccessfulVllmResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            Debug.Assert(!response.IsSuccessStatusCode, "must only be invoked for unsuccessful responses.");

            // Read the entire response content into a string.
            string errorContent =
#if NET
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            // The response content *could* be JSON formatted, try to extract the error field.

#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                using JsonDocument document = JsonDocument.Parse(errorContent);
                if (document.RootElement.TryGetProperty("error", out JsonElement errorElement) &&
                    errorElement.ValueKind is JsonValueKind.String)
                {
                    errorContent = errorElement.GetString()!;
                }
            }
            catch
            {
                // Ignore JSON parsing errors.
            }
#pragma warning restore CA1031 // Do not catch general exception types

            throw new InvalidOperationException($"Vllm error: {errorContent}");
        }
    }
}
