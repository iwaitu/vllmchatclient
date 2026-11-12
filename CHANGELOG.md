# Changelog

## 2.0.39

- Fix `VllmOpenAiGptClient` in Responses mode against hosts other than `api.openai.com` (OpenRouter and similar gateways).
  The client added the Chat Completions reasoning flags (`reasoning`, `include_reasoning`, `enable_reasoning`,
  `stream_reasoning`, all booleans) to every request; a Responses request carries them at the top level, where
  `reasoning` must be an object, so the gateway answered 400 "expected object, received boolean" and no request could
  succeed. They are no longer sent in Responses mode; `ThinkingEnabled` and `OpenAiGptChatOptions.ReasoningLevel` still
  send the `reasoning` object.
- Structured output in Responses mode now uses `text.format` on every host, not only `api.openai.com`, and the vLLM-only
  `structured_outputs` is not sent with it. A gateway that forwards to OpenAI rejects `response_format` there.
- Verified live through OpenRouter with `openai/gpt-5-nano`: a tool round trip (streaming and not), thinking enabled,
  JSON object and JSON schema output. Chat Completions requests are unchanged.

## 2.0.38

- Add an opt-in to keep sending tool definitions under `ChatToolMode.None`. Set
  `ChatOptions.AdditionalProperties["tools.keep_when_none"]` (`VllmToolDeclarations.KeepWhenToolModeNoneKey`) to `true`
  and the request carries the same `tools` as before together with `tool_choice: "none"`. By default a `None` request
  sends no `tools`; providers render tool definitions ahead of the conversation, so a turn that drops them misses the
  provider's prompt cache for everything sent so far.
- A missing key, `null` or `false` leaves requests unchanged. Any other value is rejected before a request is sent, and
  the key itself is never sent. Applies to Chat Completions, Responses and Anthropic Messages requests built by
  `VllmBaseChatClient`; clients with their own tool builders (Qwq, Gemma, Gemini 3) are unchanged. Tool calls written as
  text are still not parsed under `None`.

## 2.0.37

- Structured output against OpenAI's own API (`VllmOpenAiGptClient`, `api.openai.com`). In Responses mode the format was sent as the
  Chat Completions `response_format`, which the Responses API rejects ("moved to `text.format`"); it is now sent as `text.format`
  (`json_object`, or `json_schema` with `name`, `description`, `schema`, `strict`). In both modes the vLLM-only `extra_body`
  (`structured_outputs`) and the Ollama-style `format` are no longer sent to that host.
- Verified live against `gpt-6.1-terra` with a JSON schema, in Chat Completions and in Responses.

## 2.0.36

- Fix `VllmOpenAiGptClient` against OpenAI's own API (`api.openai.com`). It used to add OpenRouter/vLLM-style fields
  (a nested `options` object, `reasoning`, `include_reasoning`, `enable_reasoning`, `stream_reasoning`) that OpenAI answers
  with `400 unknown_parameter`, so no request could succeed. For that host the request now carries only fields the official
  Chat Completions / Responses API defines: top-level `temperature`, `top_p`, `max_completion_tokens`
  (`max_output_tokens` for Responses) and, when asked for, `reasoning_effort` (Chat Completions) or `reasoning.effort`
  (Responses). Other hosts (OpenRouter, vLLM, ...) keep their existing request shape.
- Reasoning effort on the official API: an explicit `VllmChatOptions.ReasoningEffort` or `OpenAiGptChatOptions.ReasoningLevel`
  is sent as given. `VllmChatOptions.ThinkingEnabled = false` is sent as effort `none` for GPT-5/6 models (not `-pro`, not
  o-series, which do not accept `none`). On `/chat/completions` with function tools and no explicit choice the client also
  sends `none`, because the API refuses tools there with any reasoning effort.
- GPT-5/6 and o-series models refuse `temperature` / `top_p` while they reason, so those two fields are not sent unless the
  effort is `none`.
- Verified live against `gpt-6.1-terra` (Chat Completions and Responses, streaming, with a function tool) and
  `gemini-3.8-flash` (native endpoint). Both live checks are env-gated (`VLLM_LIVE_OPENAI=1` + `OPENAI_API_KEY`,
  `VLLM_LIVE_GEMINI=1` + `GEMINI_API_KEY`) and do nothing otherwise.

## 2.0.35

- Add opt-in tool-level `strict`. Set
  `AIFunction.AdditionalProperties["strictJsonSchema"]` to `true` or `false`
  (the same key Microsoft.Extensions.AI.OpenAI reads) and the flag is sent on
  the tool definition: `tools[].function.strict` for Chat Completions,
  `tools[].strict` for Responses and Anthropic Messages. When the key is absent
  or not a boolean, `strict` is omitted and requests are unchanged.
- This only forwards the flag. Whether the schema is enforced depends on the
  backend (for vLLM `tool_choice="auto"`, on its strict-tool-calling setting),
  and the caller must supply an OpenAI-strict-compatible schema
  (`additionalProperties: false`, every property in `required`). Older vLLM
  versions rejected the `strict` field, so leave it unset for those backends.
- Clients with their own tool builders (Qwq, Gemma, Gemini 3) are not changed.

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
