using Microsoft.Extensions.AI;
using System.Text.Json;

namespace VllmChatClient.Test;

public class DeserializationTests
{
    [Fact]
    public void Claude_ReasoningDetails_ShouldDeserialize()
    {
        var json = "{\"id\":\"gen-123\",\"model\":\"anthropic/claude-opus-4.6\",\"object\":\"chat.completion\",\"created\":1770721041,\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\",\"refusal\":null,\"reasoning\":\"The user said hello\",\"reasoning_details\":[{\"format\":\"anthropic-claude-v1\",\"index\":0,\"type\":\"reasoning.text\",\"text\":\"The user said hello from details\",\"signature\":\"test\"}]}}],\"usage\":{\"prompt_tokens\":27,\"completion_tokens\":71,\"total_tokens\":98}}";

        var response = JsonSerializer.Deserialize(json, JsonContext.Default.VllmChatResponse);
        var msg = response?.Choices?.FirstOrDefault()?.Message;

        Assert.Equal("Hello", msg?.Content);
        Assert.Equal("The user said hello", msg?.Reasoning);
        Assert.NotNull(msg?.ReasoningDetails);
        Assert.Equal(1, msg.ReasoningDetails.Length);
        Assert.Equal("The user said hello from details", msg.ReasoningDetails[0].Text);
    }

    [Fact]
    public void Claude_ExactApiResponse_ShouldDeserialize()
    {
        var json = "{\"id\":\"gen-1770721041-Tuxdfddh3cyIBwOTI6Wi\",\"provider\":\"Anthropic\",\"model\":\"anthropic/claude-opus-4.6\",\"object\":\"chat.completion\",\"created\":1770721041,\"choices\":[{\"logprobs\":null,\"finish_reason\":\"stop\",\"native_finish_reason\":\"stop\",\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"你好！很高兴见到你\",\"refusal\":null,\"reasoning\":\"The user said hello\",\"reasoning_details\":[{\"format\":\"anthropic-claude-v1\",\"index\":0,\"type\":\"reasoning.text\",\"text\":\"The user said hello from reasoning_details\",\"signature\":\"EpACCkYI\"}]}}],\"usage\":{\"prompt_tokens\":27,\"completion_tokens\":71,\"total_tokens\":98}}";

        var response = JsonSerializer.Deserialize(json, JsonContext.Default.VllmChatResponse);
        var msg = response?.Choices?.FirstOrDefault()?.Message;

        Assert.Equal("你好！很高兴见到你", msg?.Content);
        Assert.Equal("The user said hello", msg?.Reasoning);
        Assert.NotNull(msg?.ReasoningDetails);
        Assert.Equal(1, msg.ReasoningDetails.Length);
        Assert.Equal("The user said hello from reasoning_details", msg.ReasoningDetails[0].Text);
    }

    [Fact]
    public void Claude_RequestSerialization_ShouldIncludeReasoning()
    {
        var request = new VllmOpenAIChatRequest
        {
            Model = "anthropic/claude-opus-4.6",
            Messages = [new VllmOpenAIChatRequestMessage { Role = "user", Content = "hello" }],
            Stream = false,
            Reasoning = new VllmReasoningOptions { Effort = "high" },
            MaxTokens = 1024
        };

        var json = JsonSerializer.Serialize(request, JsonContext.Default.VllmOpenAIChatRequest);

        Assert.Contains("\"reasoning\"", json);
        Assert.Contains("\"effort\":\"high\"", json);
        Assert.Contains("\"max_tokens\":1024", json);
    }
    
    [Fact]
    public void Claude_FullRequestJson_Format()
    {
        var request = new VllmOpenAIChatRequest
        {
            Model = "anthropic/claude-opus-4.6",
            Messages = [new VllmOpenAIChatRequestMessage { Role = "user", Content = "hello" }],
            Stream = false,
            Reasoning = new VllmReasoningOptions { Effort = "high" },
            MaxTokens = 1024
        };

        var json = JsonSerializer.Serialize(request, JsonContext.Default.VllmOpenAIChatRequest);
        
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        Assert.True(root.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("effort", out var effort));
        Assert.Equal("high", effort.GetString());
        
        // Verify enabled:false is NOT included (would confuse the API)
        Assert.DoesNotContain("\"enabled\"", json);
    }
}
