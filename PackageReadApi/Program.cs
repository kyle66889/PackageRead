using PackageReadApi.Application.Services;
using PackageReadApi.Endpoints;
using PackageReadApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GeminiConfig>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.WithOrigins(
            builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"])
         .AllowAnyHeader()
         .AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();
app.UseCors();
app.MapAnalyzeEndpoints();
app.Run();

public partial class Program { }
