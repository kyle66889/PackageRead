using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PackageReadApi.Application.Services;
using PackageReadApi.Models;
using Xunit;

namespace PackageReadApi.Tests;

public class AnalyzeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AnalyzeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IGeminiService));
                if (descriptor != null) services.Remove(descriptor);
                services.AddScoped<IGeminiService, FakeGeminiService>();
            }));
    }

    [Fact]
    public async Task PostAnalyze_Returns200WithSseContentType()
    {
        var client = _factory.CreateClient();
        var payload = new { mode = "shipping", photos = new[] { "base64data" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/analyze", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostAnalyze_ReturnsSseData()
    {
        var client = _factory.CreateClient();
        var payload = new { mode = "general", photos = new[] { "base64data" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/analyze", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("data:", body);
    }
}

internal class FakeGeminiService : IGeminiService
{
    public async Task StreamAnalyzeAsync(AnalyzeRequest request, HttpResponse response, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        await response.WriteAsync("data: {\"test\":\"ok\"}\n\n", ct);
    }
}
