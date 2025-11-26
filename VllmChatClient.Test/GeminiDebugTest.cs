using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace VllmChatClient.Test
{
    public class GeminiDebugTest
    {
        private readonly ITestOutputHelper _output;
        private readonly string _apiKey;

        public GeminiDebugTest(ITestOutputHelper output)
        {
            _output = output;
            _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        }

        [Fact]
        public async Task DebugRawGeminiResponse()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = "What is 2+2? Explain your reasoning." } }
                    }
                },
                generationConfig = new
                {
                    temperature = 1.0,
                    thinkingConfig = new
                    {
                        thinkingLevel = "high"  // 使用 high 来获取推理内容
                    }
                }
            };

            var endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent";

            try
            {
                var response = await client.PostAsJsonAsync(endpoint, requestBody);
                var rawJson = await response.Content.ReadAsStringAsync();

                _output.WriteLine("=== RAW GEMINI RESPONSE ===");
                _output.WriteLine(rawJson);

                // 尝试格式化 JSON
                try
                {
                    var jsonDoc = JsonDocument.Parse(rawJson);
                    var formattedJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                    _output.WriteLine("\n=== FORMATTED JSON ===");
                    _output.WriteLine(formattedJson);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Could not format JSON: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task DebugFunctionCallResponse()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = "What's the weather in Beijing?" } }
                    }
                },
                tools = new[]
                {
                    new
                    {
                        functionDeclarations = new[]
                        {
                            new
                            {
                                name = "get_weather",
                                description = "Get the current weather for a given location",
                                parameters = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        location = new
                                        {
                                            type = "string",
                                            description = "The city name, e.g. Beijing, Shanghai"
                                        }
                                    },
                                    required = new[] { "location" }
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    thinkingConfig = new
                    {
                        thinkingLevel = "high"
                    }
                }
            };

            var endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent";

            try
            {
                _output.WriteLine("=== FUNCTION CALL REQUEST ===");
                var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
                _output.WriteLine(requestJson);

                var response = await client.PostAsJsonAsync(endpoint, requestBody);
                var rawJson = await response.Content.ReadAsStringAsync();

                _output.WriteLine("\n=== FUNCTION CALL RESPONSE ===");
                _output.WriteLine(rawJson);

                // 分析响应结构
                try
                {
                    var jsonDoc = JsonDocument.Parse(rawJson);
                    var formattedJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                    _output.WriteLine("\n=== FORMATTED RESPONSE ===");
                    _output.WriteLine(formattedJson);

                    // 检查是否有 thoughtSignature
                    if (jsonDoc.RootElement.TryGetProperty("candidates", out var candidates))
                    {
                        _output.WriteLine("\n=== ANALYZING STRUCTURE ===");
                        var candidateArray = candidates.EnumerateArray();
                        foreach (var candidate in candidateArray)
                        {
                            if (candidate.TryGetProperty("content", out var content))
                            {
                                if (content.TryGetProperty("parts", out var parts))
                                {
                                    var partsArray = parts.EnumerateArray();
                                    int partIndex = 0;
                                    foreach (var part in partsArray)
                                    {
                                        _output.WriteLine($"\n--- Part {partIndex} ---");
                                        
                                        if (part.TryGetProperty("thoughtSignature", out var signature))
                                        {
                                            _output.WriteLine($"? Found thoughtSignature: {signature.GetString()?.Substring(0, Math.Min(50, signature.GetString()?.Length ?? 0))}...");
                                        }
                                        else
                                        {
                                            _output.WriteLine("? No thoughtSignature in this part");
                                        }

                                        if (part.TryGetProperty("functionCall", out var functionCall))
                                        {
                                            _output.WriteLine("? Found functionCall");
                                            if (functionCall.TryGetProperty("name", out var name))
                                            {
                                                _output.WriteLine($"  Function name: {name.GetString()}");
                                            }
                                        }

                                        if (part.TryGetProperty("text", out var text))
                                        {
                                            _output.WriteLine($"? Found text: {text.GetString()?.Substring(0, Math.Min(50, text.GetString()?.Length ?? 0))}...");
                                        }

                                        partIndex++;
                                    }
                                }
                            }
                        }
                    }

                    // 检查 usage metadata
                    if (jsonDoc.RootElement.TryGetProperty("usageMetadata", out var usage))
                    {
                        _output.WriteLine("\n=== USAGE METADATA ===");
                        if (usage.TryGetProperty("thoughtsTokenCount", out var thoughtsTokens))
                        {
                            _output.WriteLine($"Thoughts Token Count: {thoughtsTokens.GetInt32()}");
                        }
                        if (usage.TryGetProperty("promptTokenCount", out var promptTokens))
                        {
                            _output.WriteLine($"Prompt Token Count: {promptTokens.GetInt32()}");
                        }
                        if (usage.TryGetProperty("candidatesTokenCount", out var candidatesTokens))
                        {
                            _output.WriteLine($"Candidates Token Count: {candidatesTokens.GetInt32()}");
                        }
                        if (usage.TryGetProperty("totalTokenCount", out var totalTokens))
                        {
                            _output.WriteLine($"Total Token Count: {totalTokens.GetInt32()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Error analyzing response: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task DebugParallelFunctionCalls()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = "Check the weather in Paris and London" } }
                    }
                },
                tools = new[]
                {
                    new
                    {
                        functionDeclarations = new[]
                        {
                            new
                            {
                                name = "get_current_temperature",
                                description = "Gets the current temperature for a given location",
                                parameters = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        location = new
                                        {
                                            type = "string",
                                            description = "The city name, e.g. Paris, London"
                                        }
                                    },
                                    required = new[] { "location" }
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    thinkingConfig = new
                    {
                        thinkingLevel = "low"
                    }
                }
            };

            var endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent";

            try
            {
                _output.WriteLine("=== PARALLEL FUNCTION CALLS TEST ===");
                
                var response = await client.PostAsJsonAsync(endpoint, requestBody);
                var rawJson = await response.Content.ReadAsStringAsync();

                var jsonDoc = JsonDocument.Parse(rawJson);
                var formattedJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                _output.WriteLine(formattedJson);

                // 检查并行函数调用中的 thoughtSignature
                if (jsonDoc.RootElement.TryGetProperty("candidates", out var candidates))
                {
                    _output.WriteLine("\n=== PARALLEL FUNCTION CALL ANALYSIS ===");
                    var candidateArray = candidates.EnumerateArray();
                    foreach (var candidate in candidateArray)
                    {
                        if (candidate.TryGetProperty("content", out var content))
                        {
                            if (content.TryGetProperty("parts", out var parts))
                            {
                                var partsArray = parts.EnumerateArray();
                                int partIndex = 0;
                                foreach (var part in partsArray)
                                {
                                    _output.WriteLine($"\n--- Part {partIndex} (Expected: signature only in first part) ---");
                                    
                                    bool hasSignature = part.TryGetProperty("thoughtSignature", out var signature);
                                    bool hasFunctionCall = part.TryGetProperty("functionCall", out var functionCall);

                                    if (hasFunctionCall)
                                    {
                                        var functionName = functionCall.TryGetProperty("name", out var name) 
                                            ? name.GetString() 
                                            : "unknown";
                                        _output.WriteLine($"Function Call: {functionName}");
                                    }

                                    if (hasSignature)
                                    {
                                        var sigPreview = signature.GetString()?.Substring(0, Math.Min(30, signature.GetString()?.Length ?? 0));
                                        _output.WriteLine($"? Has thoughtSignature: {sigPreview}...");
                                        if (partIndex > 0)
                                        {
                                            _output.WriteLine("  ?? WARNING: Signature found in non-first part (unexpected for parallel calls)");
                                        }
                                    }
                                    else
                                    {
                                        _output.WriteLine("? No thoughtSignature");
                                        if (partIndex == 0 && hasFunctionCall)
                                        {
                                            _output.WriteLine("  ?? WARNING: First function call part missing signature");
                                        }
                                    }

                                    partIndex++;
                                }

                                _output.WriteLine($"\n? Total parts: {partIndex}");
                                _output.WriteLine("Expected behavior: Only first part should have thoughtSignature");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task DebugMultiTurnWithFunctionCall()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _output.WriteLine("Skipping test: GEMINI_API_KEY environment variable not set");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

            // First request - model makes function call
            var firstRequest = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = "What's the weather in Tokyo?" } }
                    }
                },
                tools = new[]
                {
                    new
                    {
                        functionDeclarations = new[]
                        {
                            new
                            {
                                name = "get_weather",
                                description = "Get weather information",
                                parameters = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        city = new
                                        {
                                            type = "string",
                                            description = "City name"
                                        }
                                    },
                                    required = new[] { "city" }
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    thinkingConfig = new
                    {
                        thinkingLevel = "low"
                    }
                }
            };

            var endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview:generateContent";

            try
            {
                _output.WriteLine("=== TURN 1: Initial Request ===");
                var response1 = await client.PostAsJsonAsync(endpoint, firstRequest);
                var json1 = await response1.Content.ReadAsStringAsync();
                
                var doc1 = JsonDocument.Parse(json1);
                _output.WriteLine(JsonSerializer.Serialize(doc1, new JsonSerializerOptions { WriteIndented = true }));

                // Extract thoughtSignature from first response
                string? extractedSignature = null;
                string? functionName = null;
                var functionArgs = new Dictionary<string, object?>();

                if (doc1.RootElement.TryGetProperty("candidates", out var candidates1))
                {
                    foreach (var candidate in candidates1.EnumerateArray())
                    {
                        if (candidate.TryGetProperty("content", out var content))
                        {
                            if (content.TryGetProperty("parts", out var parts))
                            {
                                foreach (var part in parts.EnumerateArray())
                                {
                                    if (part.TryGetProperty("thoughtSignature", out var sig))
                                    {
                                        extractedSignature = sig.GetString();
                                        _output.WriteLine($"\n? Extracted thoughtSignature (length: {extractedSignature?.Length})");
                                    }

                                    if (part.TryGetProperty("functionCall", out var fc))
                                    {
                                        if (fc.TryGetProperty("name", out var n))
                                        {
                                            functionName = n.GetString();
                                        }
                                        if (fc.TryGetProperty("args", out var args))
                                        {
                                            foreach (var arg in args.EnumerateObject())
                                            {
                                                functionArgs[arg.Name] = arg.Value.GetString();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(extractedSignature))
                {
                    _output.WriteLine("?? No thoughtSignature found in first response");
                    return;
                }

                if (string.IsNullOrEmpty(functionName))
                {
                    _output.WriteLine("?? No function call found in first response");
                    return;
                }

                _output.WriteLine($"\n=== TURN 2: Sending Function Result with Signature ===");
                _output.WriteLine($"Function: {functionName}");
                _output.WriteLine($"Args: {JsonSerializer.Serialize(functionArgs)}");

                // Second request - return function result WITH signature
                var secondRequest = new
                {
                    contents = new object[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = "What's the weather in Tokyo?" } }
                        },
                        new
                        {
                            role = "model",
                            parts = new[]
                            {
                                new
                                {
                                    functionCall = new
                                    {
                                        name = functionName,
                                        args = functionArgs
                                    },
                                    thoughtSignature = extractedSignature  // ? KEY: Pass back signature
                                }
                            }
                        },
                        new
                        {
                            role = "user",
                            parts = new[]
                            {
                                new
                                {
                                    functionResponse = new
                                    {
                                        name = functionName,
                                        response = new
                                        {
                                            temperature = "22°C",
                                            condition = "Sunny"
                                        }
                                    }
                                }
                            }
                        }
                    },
                    tools = new[]
                    {
                        new
                        {
                            functionDeclarations = new[]
                            {
                                new
                                {
                                    name = "get_weather",
                                    description = "Get weather information",
                                    parameters = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            city = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var response2 = await client.PostAsJsonAsync(endpoint, secondRequest);
                var json2 = await response2.Content.ReadAsStringAsync();
                
                _output.WriteLine("\n=== TURN 2 Response ===");
                var doc2 = JsonDocument.Parse(json2);
                _output.WriteLine(JsonSerializer.Serialize(doc2, new JsonSerializerOptions { WriteIndented = true }));

                _output.WriteLine("\n? Multi-turn function call with signature successful!");

            }
            catch (HttpRequestException ex)
            {
                _output.WriteLine($"\n? HTTP Error: {ex.Message}");
                if (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    _output.WriteLine("This might be due to missing or incorrect thoughtSignature");
                }
                throw;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n? Error: {ex.Message}");
                throw;
            }
        }
    }
}
