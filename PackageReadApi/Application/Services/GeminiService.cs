using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PackageReadApi.Models;

namespace PackageReadApi.Application.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiConfig _config;

    private static readonly Dictionary<string, string> Prompts = new()
    {
        ["shipping"] = """
            Extract ALL information visible on this shipping/waybill label. Return ONLY valid JSON, no markdown:
            {
              "tracking_number": "tracking or waybill number",
              "carrier": "carrier name (FedEx/UPS/DHL/SF/JD/EMS/USPS etc)",
              "service": "service type or class",
              "sender_name": "sender name or company",
              "sender_address": "full sender address",
              "sender_phone": "sender phone or null",
              "recipient_name": "recipient name or company",
              "recipient_address": "full recipient address",
              "recipient_phone": "recipient phone or null",
              "weight": "weight with unit or null",
              "ship_date": "shipping date or null",
              "cod_amount": "cash on delivery amount or null",
              "notes": "any other important info or null"
            }
            """,
        ["product"] = """
            You are a professional product inspector. Examine ALL provided images of the same product from every angle.
            Identify every label, barcode, and marking. Return ONLY valid JSON, no markdown:
            {
              "product_name": "full product name and description",
              "brand": "brand or manufacturer",
              "model_number": "model number or part number or null",
              "upc_code": "12 or 13 digit UPC/EAN barcode — read it carefully from barcode image, or null",
              "serial_number": "serial number labeled S/N, SN, Serial No., or null",
              "sku": "SKU or item number if visible, else null",
              "category": "product category",
              "condition": "good/defective/warning/unknown",
              "defects": ["describe each visible defect in full detail"],
              "color": "color(s)",
              "dimensions": "size or weight from label if visible, else null",
              "packaging_condition": "sealed / open / damaged / missing",
              "quantity": "unit count or package quantity",
              "label_text": "all text visible on any label or packaging",
              "notes": "comprehensive QC assessment referencing observations from all provided images"
            }
            """,
        ["general"] = """
            Analyze this image and extract all key information. Return ONLY valid JSON, no markdown:
            {
              "title": "brief descriptive title",
              "type": "object type or category",
              "text_content": "all visible text, numbers, codes or null",
              "key_info": ["important detail 1", "important detail 2"],
              "notes": "overall description"
            }
            """
    };

    public GeminiService(HttpClient httpClient, IOptions<GeminiConfig> options)
    {
        _httpClient = httpClient;
        _config = options.Value;
    }

    public string GetPromptForMode(string mode) =>
        Prompts.TryGetValue(mode, out var prompt)
            ? prompt
            : throw new ArgumentException($"Unknown mode: {mode}", nameof(mode));

    public async Task StreamAnalyzeAsync(AnalyzeRequest request, HttpResponse response, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        var prompt = GetPromptForMode(request.Mode);

        var parts = new JsonArray();
        foreach (var photo in request.Photos)
        {
            parts.Add(new JsonObject
            {
                ["inline_data"] = new JsonObject
                {
                    ["mime_type"] = "image/jpeg",
                    ["data"] = photo
                }
            });
        }
        parts.Add(new JsonObject { ["text"] = prompt });

        var body = new JsonObject
        {
            ["contents"] = new JsonArray { new JsonObject { ["parts"] = parts } },
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.1,
                ["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = 0 },
                ["maxOutputTokens"] = 1200
            }
        };

        var requestContent = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var url = $"v1beta/models/{_config.Model}:streamGenerateContent?alt=sse&key={_config.ApiKey}";

        HttpResponseMessage httpResp;
        try
        {
            httpResp = await _httpClient.PostAsync(url, requestContent, ct);
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(response, ex.Message, ct);
            return;
        }

        if (!httpResp.IsSuccessStatusCode)
        {
            var err = await httpResp.Content.ReadAsStringAsync(ct);
            var snippet = err.Length > 300 ? err[..300] : err;
            await WriteErrorAsync(response, $"API {(int)httpResp.StatusCode}: {snippet}", ct);
            return;
        }

        using var stream = await httpResp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            await response.WriteAsync(line + "\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }

    private static async Task WriteErrorAsync(HttpResponse response, string message, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { error = message });
        await response.WriteAsync($"data: {payload}\n\n", ct);
    }
}
