using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PackageReadApi.Application.Services;
using PackageReadApi.Models;
using System.Text.Json;
using Xunit;

namespace PackageReadApi.Tests;

public class GeminiServiceTests
{
    [Fact]
    public void AnalyzeRequest_DeserializesFromJson()
    {
        var json = """{"mode":"shipping","photos":["abc123","def456"]}""";
        var result = JsonSerializer.Deserialize<AnalyzeRequest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);
        Assert.Equal("shipping", result.Mode);
        Assert.Equal(2, result.Photos.Count);
        Assert.Equal("abc123", result.Photos[0]);
    }

    // ── helpers shared by later tests ────────────────────────────────────────

    internal static GeminiService BuildService(HttpMessageHandler? handler = null)
    {
        var h = handler ?? new OkSseHandler("data: [DONE]\n\n");
        var client = new HttpClient(h) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
        var config = new GeminiConfig { ApiKey = "test-key", Model = "gemini-2.5-flash" };
        return new GeminiService(client, Options.Create(config));
    }

    internal class OkSseHandler : HttpMessageHandler
    {
        private readonly string _body;
        public OkSseHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            });
    }

    internal class ErrorHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public ErrorHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
    }
}
