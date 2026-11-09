using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Extensions.AI;

namespace VllmChatClient.Test;

/// <summary>
/// Live check against api.openai.com. Runs only when OPENAI_API_KEY is set and VLLM_LIVE_OPENAI=1 (model from
/// VLLM_LIVE_OPENAI_MODEL, default gpt-5.4-mini); otherwise it returns without calling anything.
/// </summary>
public class OfficialOpenAiLiveTests
{
    private static (string Key, string Model)? Settings()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable("VLLM_LIVE_OPENAI") != "1")
        {
            return null;
        }

        return (key, Environment.GetEnvironmentVariable("VLLM_LIVE_OPENAI_MODEL") ?? "gpt-5.4-mini");
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    public async Task Streaming_WithSamplingAndATool_IsAccepted_AndCallsTheTool(VllmApiMode mode)
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        var client = new VllmOpenAiGptClient("https://api.openai.com/v1/{1}", settings.Key, settings.Model, null, mode);
        var tool = AIFunctionFactory.Create((string city) => "sunny", new AIFunctionFactoryOptions { Name = "get_weather", Description = "Get the weather for a city." });
        var options = new ChatOptions { Temperature = 0.6f, MaxOutputTokens = 2000, Tools = [tool], ToolMode = ChatToolMode.RequireAny };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "What is the weather in Paris? Use the tool.")], options))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, update => update.Contents.OfType<FunctionCallContent>().Any(call => call.Name == "get_weather"));
    }

    [Theory]
    [InlineData(VllmApiMode.ChatCompletions)]
    [InlineData(VllmApiMode.Responses)]
    public async Task StructuredOutput_JsonSchema_IsAccepted_AndReturnsMatchingJson(VllmApiMode mode)
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        var client = new VllmOpenAiGptClient("https://api.openai.com/v1/{1}", settings.Key, settings.Model, null, mode);
        var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}""").RootElement;
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Return the capital of France.")],
            new VllmChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "capital"), ThinkingEnabled = false });

        using var parsed = System.Text.Json.JsonDocument.Parse(response.Text);
        Assert.True(parsed.RootElement.TryGetProperty("city", out _));
    }

    // The same real-API check for Gemini (native endpoint). Needs GEMINI_API_KEY and VLLM_LIVE_GEMINI=1; model from
    // VLLM_LIVE_GEMINI_MODEL, default gemini-3.8-flash.
    [Fact]
    public async Task Gemini_Streaming_WithSamplingAndATool_IsAccepted_AndCallsTheTool()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable("VLLM_LIVE_GEMINI") != "1")
        {
            return;
        }

        var model = Environment.GetEnvironmentVariable("VLLM_LIVE_GEMINI_MODEL") ?? "gemini-3.8-flash";
        var client = new VllmGemini3ChatClient("https://generativelanguage.googleapis.com/v1beta", key, model);
        var tool = AIFunctionFactory.Create((string city) => "sunny", new AIFunctionFactoryOptions { Name = "get_weather", Description = "Get the weather for a city." });
        var options = new VllmChatOptions { Temperature = 0.6f, MaxOutputTokens = 2000, Tools = [tool], ThinkingEnabled = false };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "What is the weather in Paris? Use the tool.")], options))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, update => update.Contents.OfType<FunctionCallContent>().Any(call => call.Name == "get_weather"));

        var plain = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Say hi in one word.")], new VllmChatOptions { Temperature = 0.6f, ThinkingEnabled = false }))
        {
            plain.Add(update);
        }

        Assert.Contains(plain, update => update.Contents.OfType<TextContent>().Any(text => !string.IsNullOrWhiteSpace(text.Text)));
    }
}
