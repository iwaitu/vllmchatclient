using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VllmChatClient.Test;

public class ResponseFormatRequestTests
{
    [Fact]
    public async Task BaseClient_JsonSchemaResponseFormat_ShouldUseResponseFormatAndStructuredOutputs()
    {
        string? requestJson = null;
        var handler = new CaptureHttpMessageHandler(async request =>
        {
            requestJson = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"resp-1\",\"created\":1,\"model\":\"test-model\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"{}\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new TestVllmChatClient("http://localhost:8000/{0}/{1}", httpClient);
        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              },
              "required": ["name"]
            }
            """).RootElement.Clone();

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Return a JSON object.")],
            new ChatOptions
            {
                ModelId = "test-model",
                ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "person", "A single person payload")
            });

        Assert.NotNull(requestJson);

        using var doc = JsonDocument.Parse(requestJson!);
        var root = doc.RootElement;
        var responseFormat = root.GetProperty("response_format");
        var jsonSchema = responseFormat.GetProperty("json_schema");
        var structuredOutputs = root.GetProperty("extra_body").GetProperty("structured_outputs");

        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.Equal("person", jsonSchema.GetProperty("name").GetString());
        Assert.Equal("A single person payload", jsonSchema.GetProperty("description").GetString());
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", jsonSchema.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("object", structuredOutputs.GetProperty("json").GetProperty("type").GetString());
    }

    private sealed class TestVllmChatClient : VllmBaseChatClient
    {
        public TestVllmChatClient(string endpoint, HttpClient httpClient)
            : base(endpoint, token: null, modelId: "test-model", httpClient)
        {
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
