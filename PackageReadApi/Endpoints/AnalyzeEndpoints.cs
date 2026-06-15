using PackageReadApi.Application.Services;
using PackageReadApi.Models;

namespace PackageReadApi.Endpoints;

public static class AnalyzeEndpoints
{
    public static void MapAnalyzeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/analyze", async (
            AnalyzeRequest request,
            IGeminiService geminiService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            await geminiService.StreamAnalyzeAsync(request, httpContext.Response, ct);
        });
    }
}
