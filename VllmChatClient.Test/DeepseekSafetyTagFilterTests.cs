using Microsoft.Extensions.AI;

namespace VllmChatClient.Test
{
    public class DeepseekSafetyTagFilterTests
    {
        private static string Run(params string[] chunks)
        {
            var filter = new DeepseekSafetyTagFilter();
            var sb = new System.Text.StringBuilder();
            foreach (var c in chunks)
            {
                sb.Append(filter.Push(c));
            }

            sb.Append(filter.Flush());
            return sb.ToString();
        }

        [Fact]
        public void PlainText_IsUntouched() =>
            Assert.Equal("a < b and <div> ok", Run("a < b and ", "<div> ok"));

        [Fact]
        public void WholeBlock_InSingleChunk_IsRemoved() =>
            Assert.Equal("before after",
                Run("before <ds_safety>[用户未成年]否\n[规则]无</ds_safety>Safe\n\nafter"));

        [Fact]
        public void Block_SplitLikeAliyunStream_IsRemoved() =>
            // chunk boundaries observed from deepseek-v4.1-flash-1017 streaming
            Assert.Equal("I should respond warmly. Hi!",
                Run("I should respond warmly. ", "<", "_safety".Insert(0, "ds"), ">[用户未成年", "]否\n[规则]无</ds_s", "afety>Safe\n", "\n", "Hi!"));

        [Fact]
        public void Block_SplitAtEveryCharacter_LeavesNoResidue()
        {
            const string input = "start <ds_safety>secret</ds_safety>Safe\n\nanswer";
            Assert.Equal("start answer", Run(input.Select(c => c.ToString()).ToArray()));
        }

        [Fact]
        public void TrailingPartialOpenTag_IsEmittedOnFlush() =>
            Assert.Equal("value <ds_saf", Run("value <ds_", "saf"));

        [Fact]
        public void UnterminatedBlock_IsDropped() =>
            Assert.Equal("x ", Run("x <ds_safety>never closed"));

        [Fact]
        public void TextAfterCloseTag_WithoutSafeMarker_IsKept() =>
            Assert.Equal("a b", Run("a <ds_safety>x</ds_safety>b"));

        [Fact]
        public void Strip_NonStreamed() =>
            Assert.Equal("Hi! How can I help",
                DeepseekSafetyTagFilter.Strip("Hi! <ds_safety>[分类]其他</ds_safety>Safe\n\nHow can I help"));
    }
}
