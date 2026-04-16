using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace VllmChatClient.Test;

public class OpenAiGptJsonSchemaTests
{
    private readonly IChatClient _client;
    private readonly ITestOutputHelper _output;
    private readonly bool _skipTests;

    public OpenAiGptJsonSchemaTests(ITestOutputHelper output)
    {
        _output = output;
        var apiKey = Environment.GetEnvironmentVariable("OPEN_ROUTE_API_KEY");
        _skipTests = string.IsNullOrWhiteSpace(apiKey);
        _client = new VllmOpenAiGptClient("https://openrouter.ai/api/v1", apiKey, "openai/gpt-5.2-codex");
    }

    [Fact]
    public async Task JsonSchemaOutputTest()
    {
        if (_skipTests)
        {
            return;
        }

        var options = new OpenAiGptChatOptions
        {
            ReasoningLevel = OpenAiGptReasoningLevel.High,
            MaxOutputTokens = 300,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                StructuredJsonSchemaTestHelper.CreateGreetingSchema(),
                "greeting_payload",
                "Greeting payload")
        };

        var res = await _client.GetResponseAsync(StructuredJsonSchemaTestHelper.CreateGreetingMessages(), options);
        Assert.NotNull(res);
        Assert.Single(res.Messages);
        StructuredJsonSchemaTestHelper.AssertGreetingJson(res.Text);
        _output.WriteLine($"Response: {res.Text}");
    }
}
