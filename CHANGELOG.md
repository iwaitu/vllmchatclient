# Changelog

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
