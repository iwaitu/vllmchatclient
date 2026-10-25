// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Extensions.AI
{
    /// <summary>
    /// Removes the <c>&lt;ds_safety&gt;…&lt;/ds_safety&gt;</c> moderation block (and the trailing <c>Safe</c> marker)
    /// that some DeepSeek V4.1 snapshots leak into reasoning/answer text. In streaming mode the tag is split across
    /// several chunks, so the filter keeps a small look-behind buffer. One instance per text channel per stream.
    /// </summary>
    internal sealed class DeepseekSafetyTagFilter
    {
        private const string OpenTag = "<ds_safety>";
        private const string CloseTag = "</ds_safety>";
        private const string SafeMarker = "Safe";

        private enum State { Normal, InsideTag, ExpectSafeMarker, SkipWhitespace }

        private readonly StringBuilder _pending = new();
        private State _state = State.Normal;

        public static bool MayContainTag(string? text) =>
            text is not null && text.IndexOf("ds_safety", StringComparison.Ordinal) >= 0;

        /// <summary>Strips the tag block from a complete (non-streamed) text.</summary>
        public static string Strip(string text)
        {
            if (!MayContainTag(text))
            {
                return text;
            }

            var filter = new DeepseekSafetyTagFilter();
            return filter.Push(text) + filter.Flush();
        }

        /// <summary>Feeds the next chunk and returns the text that is safe to emit now.</summary>
        public string Push(string chunk)
        {
            _pending.Append(chunk);
            var output = new StringBuilder();

            while (_pending.Length > 0)
            {
                var text = _pending.ToString();

                switch (_state)
                {
                    case State.Normal:
                    {
                        int open = text.IndexOf(OpenTag, StringComparison.Ordinal);
                        if (open >= 0)
                        {
                            output.Append(text, 0, open);
                            Reset(text.Substring(open + OpenTag.Length));
                            _state = State.InsideTag;
                            continue;
                        }

                        int keep = PartialSuffixLength(text, OpenTag);
                        output.Append(text, 0, text.Length - keep);
                        Reset(text.Substring(text.Length - keep));
                        return output.ToString();
                    }

                    case State.InsideTag:
                    {
                        int close = text.IndexOf(CloseTag, StringComparison.Ordinal);
                        if (close >= 0)
                        {
                            Reset(text.Substring(close + CloseTag.Length));
                            _state = State.ExpectSafeMarker;
                            continue;
                        }

                        // Drop the tag body, keep only a possible start of the closing tag.
                        Reset(text.Substring(text.Length - PartialSuffixLength(text, CloseTag)));
                        return output.ToString();
                    }

                    case State.ExpectSafeMarker:
                    {
                        if (text.StartsWith(SafeMarker, StringComparison.Ordinal))
                        {
                            Reset(text.Substring(SafeMarker.Length));
                            _state = State.SkipWhitespace;
                            continue;
                        }

                        if (SafeMarker.StartsWith(text, StringComparison.Ordinal))
                        {
                            return output.ToString(); // need more input to decide
                        }

                        _state = State.Normal;
                        continue;
                    }

                    default: // SkipWhitespace
                    {
                        string trimmed = text.TrimStart();
                        Reset(trimmed);
                        if (trimmed.Length > 0)
                        {
                            _state = State.Normal;
                        }

                        break;
                    }
                }
            }

            return output.ToString();
        }

        /// <summary>Returns text held back as a potential tag prefix once the stream has ended.</summary>
        public string Flush()
        {
            string rest = _state == State.Normal ? _pending.ToString() : string.Empty;
            _pending.Clear();
            _state = State.Normal;
            return rest;
        }

        private void Reset(string value)
        {
            _pending.Clear();
            _pending.Append(value);
        }

        private static int PartialSuffixLength(string text, string tag)
        {
            int max = Math.Min(text.Length, tag.Length - 1);
            for (int len = max; len > 0; len--)
            {
                if (string.CompareOrdinal(text, text.Length - len, tag, 0, len) == 0)
                {
                    return len;
                }
            }

            return 0;
        }
    }
}
