using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class Gemini3ReproductionTest
    {
        private readonly ITestOutputHelper _output;

        public Gemini3ReproductionTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ParallelFunctionCall_Verification()
        {
            // Mock HttpClient to simulate Gemini API response
            var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            
            // Construct a response that mimics Gemini 3 parallel tool calls
            // One thoughtSignature, followed by two function calls
            var geminiResponse = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            role = "model",
                            parts = new object[]
                            {
                                new
                                {
                                    thoughtSignature = "signature_12345"
                                },
                                new
                                {
                                    functionCall = new
                                    {
                                        name = "GetWeather",
                                        args = new { city = "Beijing" }
                                    }
                                },
                                new
                                {
                                    functionCall = new
                                    {
                                        name = "GetWeather",
                                        args = new { city = "Shanghai" }
                                    }
                                }
                            }
                        },
                        finishReason = "STOP",
                        usageMetadata = new
                        {
                            promptTokenCount = 10,
                            candidatesTokenCount = 20,
                            totalTokenCount = 30
                        }
                    }
                }
            };

            var jsonResponse1 = JsonSerializer.Serialize(geminiResponse);
            
            // Second response (Final answer)
            var geminiResponse2 = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            role = "model",
                            parts = new object[]
                            {
                                new { text = "The weather in Beijing is Sunny and Shanghai is Cloudy." }
                            }
                        },
                        finishReason = "STOP"
                    }
                }
            };
            var jsonResponse2 = JsonSerializer.Serialize(geminiResponse2);

            // Setup sequence
            mockHttpMessageHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse1, Encoding.UTF8, "application/json")
                })
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse2, Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(mockHttpMessageHandler.Object);
            var client = new VllmGemini3ChatClient(
                "https://generativelanguage.googleapis.com/v1beta",
                "fake_key",
                "gemini-3-pro-preview",
                httpClient
            );

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Check weather in Beijing and Shanghai")
            };

            var options = new GeminiChatOptions
            {
                Tools = new List<AITool>
                {
                    AIFunctionFactory.Create((string city) => $"Weather in {city}", "GetWeather")
                }
            };

            // Act
            // Turn 1
            var response = await client.GetResponseAsync(messages, options);

            // Assert Turn 1
            Assert.NotNull(response);
            var functionCalls = response.Messages[0].Contents.OfType<FunctionCallContent>().ToList();

            Assert.Equal(2, functionCalls.Count);

            foreach (var fc in functionCalls)
            {
                 _output.WriteLine($"Function: {fc.Name}");
                 _output.WriteLine($"  Call ID: {fc.CallId}");
                 _output.WriteLine($"  Arguments: {System.Text.Json.JsonSerializer.Serialize(fc.Arguments)}");
                    
                 if (fc.AdditionalProperties?.ContainsKey("thoughtSignature") == true)
                 {
                      var sig = fc.AdditionalProperties["thoughtSignature"]?.ToString();
                      var displaySig = sig?.Length > 20 ? sig.Substring(0, 20) + "..." : sig;
                      _output.WriteLine($"  thoughtSignature: [Present] {displaySig}");
                 }
                 else 
                 {
                      _output.WriteLine($"  thoughtSignature: [Absent]");
                 }
                 _output.WriteLine("");
            }

            // Verify first function call
            var firstCall = functionCalls[0];
            Assert.Equal("GetWeather", firstCall.Name);
            Assert.Equal("Beijing", (firstCall.Arguments?["city"] as JsonElement?)?.GetString());
            
            // Verify thoughtSignature on first call
            Assert.True(firstCall.AdditionalProperties?.ContainsKey("thoughtSignature") == true, "First call should have thoughtSignature");
            Assert.Equal("signature_12345", firstCall.AdditionalProperties?["thoughtSignature"]);
            Assert.False(firstCall.Arguments?.ContainsKey("thoughtSignature") ?? false, "thoughtSignature should NOT be in Arguments of first call");

            // Verify second function call
            var secondCall = functionCalls[1];
            Assert.Equal("GetWeather", secondCall.Name);
            Assert.Equal("Shanghai", (secondCall.Arguments?["city"] as JsonElement?)?.GetString());

            // Verify thoughtSignature is NOT on second call
            Assert.False(secondCall.AdditionalProperties?.ContainsKey("thoughtSignature") == true, "Second call should NOT have thoughtSignature in AdditionalProperties");
            Assert.False(secondCall.Arguments?.ContainsKey("thoughtSignature") ?? false, "thoughtSignature should NOT be in Arguments of second call");
            
            _output.WriteLine("✓ Verified: Reproduction test confirms thoughtSignature is only on the first call.");

            // Act Turn 2 (Execute tools and get final response)
            messages.Add(response.Messages[0]);
            foreach (var fc in functionCalls)
            {
                var city = (fc.Arguments?["city"] as JsonElement?)?.GetString() ?? "";
                var result = $"Weather in {city}"; // Mock result
                messages.Add(new ChatMessage(ChatRole.User, new List<AIContent> 
                { 
                    new FunctionResultContent(fc.CallId, result) 
                }));
            }

            var finalResponse = await client.GetResponseAsync(messages, options);
            _output.WriteLine($"Final Response: {finalResponse.Text}");
            Assert.Contains("Sunny", finalResponse.Text);
        }
    }
}
