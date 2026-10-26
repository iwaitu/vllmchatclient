# Changelog

## 2.0.34

- In `VllmGlmChatClient` Chat Completions, map `ThinkingEnabled=false` to
  enabled thinking with `reasoning_effort=low` for exact GLM-5.3-Flash model
  IDs, including the optional `ZHIPU/` prefix and per-call model overrides.
  This is low-effort reasoning, not a guarantee of zero reasoning tokens.
- Preserve explicit `low`, `high`, or `max` effort when thinking is enabled;
  reject unsupported non-empty effort values before sending HTTP. Keep older
  model behavior and caller-owned options unchanged.
- Add offline request coverage for streaming, tools, history replay, model
  overrides, and repeated calls. Include these tests in build and release CI.

## 2.0.33

- Add an explicit, opt-in Anthropic prompt-cache setting for the native
  Messages mode: put `new VllmAnthropicCacheControl("5m")` (or `"1h"`, or no
  ttl) in `ChatOptions.AdditionalProperties["anthropic.cache_control"]` and the
  request carries a top-level `cache_control`. Caching is never enabled by
  default; a missing key keeps the request unchanged and `null` switches it
  off. Any other value type, and a non-null setting with another API mode, is
  rejected before HTTP.
- Unify provider usage metering across Chat Completions, Responses and
  Anthropic Messages (streaming and non-streaming), shared with Sonnet 5.5.
  Missing counters stay `null` instead of becoming zero, explicit zeros are
  kept, cache read/creation (and 5m/1h) counters are preserved in
  `UsageDetails.AdditionalCounts`, and invalid counters raise
  `InvalidOperationException("Invalid provider usage counters.")`.
- **Behavior change:** for every Anthropic Messages client,
  `InputTokenCount` is now uncached + cache read + cache creation (the old
  value was the raw `input_tokens` only). The raw value is still available as
  `AdditionalCounts["input_tokens"]`. Responses that return no cache fields are
  numerically unchanged. For Sonnet 5.5 a missing counter is now `null` where it
  used to default to 0; complete responses are unchanged.
- Chat Completions and Responses keep the provider's total input unchanged and
  expose `prompt_tokens_details.cached_tokens` /
  `input_tokens_details.cached_tokens` (plus the alias `cached_input_tokens`)
  only in `AdditionalCounts`; they are never added again.
- Streaming emits exactly one usage update per normally completed response
  (the same `UsageDetails` in a single `UsageContent` and in
  `UsageChatResponseUpdate.Usage`), before the finish update. A stream that
  ends without its termination marker still emits its known finish update but
  no usage. `VllmClaudeChatClient` Chat Completions responses and the other
  clients that parse Chat Completions usage themselves use the same rules.
- Add offline wire, public-interface and NativeAOT regression coverage.

## 2.0.32

- Adapt the existing JSON Schema response-format interface to JSON Object in
  `VllmDeepseekV4ChatClient` for exact `deepseek-v4.1-flash-1017`, including
  effective model overrides. Preserve schema instructions, tool schemas,
  caller-owned inputs and derived options.
- Map each protocol to its own JSON Object field and expose the weaker
  provider guarantee through response/update metadata. No additional model
  calls, retries, or automatic protocol changes.
- Reject known incompatible root schemas and explicit output-format
  extension conflicts before HTTP. Keep other clients/model IDs unchanged.
- Add offline wire, public-interface and NativeAOT regression coverage.

Earlier release notes are retained in README.md.
