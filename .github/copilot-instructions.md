# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- The new client class name should be `VllmOpenAiGptClient`.
- For Qwen3.5 provider compatibility, use the following request format based on the API URL:
  - Use top-level `enable_thinking` for `aliyuncs.com` official endpoints.
  - Otherwise, use `chat_template_kwargs.enable_thinking`.
- For Gemma/Google model clients, detect the endpoint type from the URL and use Google-native request formatting for Google native URLs; otherwise, use the vLLM-compatible format.