using PackageReadApi.Models;

namespace PackageReadApi.Application.Services;

public interface IGeminiService
{
    Task StreamAnalyzeAsync(AnalyzeRequest request, HttpResponse response, CancellationToken ct);
}
