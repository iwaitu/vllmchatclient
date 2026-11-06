using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace VllmChatClient.Test;

/// <summary>
/// Live end-to-end check for tool-level strict against a real vLLM endpoint.
/// Runs only when VLLM_E2E_ENDPOINT, VLLM_E2E_KEY and VLLM_E2E_MODEL are set; no secrets live in the repo.
/// </summary>
public class ToolStrictLiveTests
{
    private sealed class Live : VllmBaseChatClient
    {
        public Live(string endpoint, string key, string model, HttpClient http)
            : base(endpoint, key, model, http, VllmApiMode.ChatCompletions) { }
    }

    private static (string Endpoint, string Key, string Model)? Config()
    {
        var endpoint = Environment.GetEnvironmentVariable("VLLM_E2E_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("VLLM_E2E_KEY");
        var model = Environment.GetEnvironmentVariable("VLLM_E2E_MODEL");
        return string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model)
            ? null
            : (endpoint, key, model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Live_ToolCall_WithStrictFlag_RoundTrips(bool? strict)
    {
        if (Config() is not { } cfg)
        {
            return; // Not configured: offline runs skip the live check.
        }

        string? sent = null;
        // VLLM_E2E_VIA_CURL=1 routes transport through curl.exe for hosts where only curl can reach the endpoint
        // (per-process network routing). The SDK still builds the request and parses the response.
        HttpMessageHandler transport = Environment.GetEnvironmentVariable("VLLM_E2E_VIA_CURL") == "1"
            ? new CurlHandler()
            : new HttpClientHandler();
        using var http = new HttpClient(new CaptureHandler(transport, body => sent = body)) { Timeout = TimeSpan.FromMinutes(3) };
        using var client = new Live(cfg.Endpoint, cfg.Key, cfg.Model, http);

        var props = new Dictionary<string, object?>();
        if (strict is { } flag)
        {
            props["strictJsonSchema"] = flag;
        }

        var tool = AIFunctionFactory.Create(
            ([Description("City name")] string city, [Description("Number of guests")] int partySize) => $"booked:{city}:{partySize}",
            new AIFunctionFactoryOptions { Name = "book_table", Description = "Book a restaurant table.", AdditionalProperties = props });

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Book a table in Paris for 4 people.")],
            new ChatOptions { Tools = [tool], Temperature = 0f, MaxOutputTokens = 300 });

        Assert.NotNull(sent);
        using var doc = System.Text.Json.JsonDocument.Parse(sent!);
        var function = doc.RootElement.GetProperty("tools")[0].GetProperty("function");
        if (strict is { } expected)
        {
            Assert.Equal(expected, function.GetProperty("strict").GetBoolean());
        }
        else
        {
            Assert.False(function.TryGetProperty("strict", out _));
        }

        var call = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("book_table", call.Name);
        Assert.Equal("Paris", call.Arguments!["city"]!.ToString());
        Assert.Equal("4", call.Arguments["partySize"]!.ToString());
    }

    private sealed class CurlHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bodyFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(bodyFile, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), cancellationToken);
                var psi = new System.Diagnostics.ProcessStartInfo("curl.exe")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                foreach (var arg in new[] { "-s", "-m", "150", "-X", request.Method.Method, "-w", "\n%{http_code}" })
                {
                    psi.ArgumentList.Add(arg);
                }

                foreach (var header in request.Headers)
                {
                    psi.ArgumentList.Add("-H");
                    psi.ArgumentList.Add($"{header.Key}: {string.Join(",", header.Value)}");
                }

                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Content-Type: application/json");
                psi.ArgumentList.Add("--data-binary");
                psi.ArgumentList.Add("@" + bodyFile);
                psi.ArgumentList.Add(request.RequestUri!.ToString());

                using var process = System.Diagnostics.Process.Start(psi)!;
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var split = output.LastIndexOf('\n');
                var status = int.Parse(output[(split + 1)..]);
                return new HttpResponseMessage((System.Net.HttpStatusCode)status)
                {
                    Content = new StringContent(output[..split], System.Text.Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                File.Delete(bodyFile);
            }
        }
    }

    private sealed class CaptureHandler(HttpMessageHandler inner, Action<string> capture) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                capture(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
