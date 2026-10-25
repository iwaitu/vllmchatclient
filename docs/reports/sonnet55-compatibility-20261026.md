# Sonnet 5.5 native Messages compatibility — 2.0.31

Sonnet 5.5 rejects the earlier `thinking: disabled` setting and forced tool
selection. Its signed progress blocks must survive application history replay.
The fix applies only to the bounded Sonnet 5.5 family in native Messages mode;
other model families and API modes retain their previous mappings.

The approved second-round plan is
`docs/plans/sonnet55-compatibility-20261026.md`, SHA256
`ad7e780eb7451c5ed06b879e639dc09e25149a6a85102d9e240e144fa8c3f983`.
Codex, Claude and Antigravity approved that exact version. Claude implemented
the isolated SDK changes; Root executed every test with model credentials
removed and the default public HTTP proxy blocked. Antigravity independently
reviewed the final code. No live provider call was part of these checks.

Validation:

- The exact offline CI whitelist passes 455 tests, including 65 pure-fake
  Sonnet tests. The first iteration exposed a display-only fixture mismatch;
  actual native execution then exposed missing standard `UsageContent` in
  stream aggregation. Both were corrected and covered by assertions.
- The win-x64 NativeAOT executable builds with reflection-based JSON disabled
  and runs signed/redacted nonstream and stream replay, block order, unknown
  fields, tool arguments and cumulative start/delta usage assertions. Existing
  V4 native replay smoke also passes.
- True/false/unset thinking, unsupported forced tool choice before HTTP,
  malformed opaque data, unknown block types, cancellation, multiple signed
  blocks, standard MEAI aggregation and cache accounting have fake coverage.

Thinking=false means between-tools progress can still occur. Applications must
retain opaque `ProtectedData` intact and in order; the SDK does not make
conversation-prefix edits or compression safe. Forced-tool users must select
Auto explicitly and validate the required action themselves. Real Sonnet
workflow acceptance remains a downstream step after official package identity
and QRE-consumer verification; these offline results are not five completed
generation tasks. DeepSeek's provider JSON-Schema capability issue is outside
this release and will be addressed after the Sonnet task batch.

Primary protocol references:

- [Sonnet 5.5 overview](https://platform.claude.com/docs/en/models/sonnet-5-5/overview)
- [Sonnet 5.5 changes](https://platform.claude.com/docs/en/models/sonnet-5-5/whats-new-sonnet-5-5)
