using SecureHub.Core;
using SecureHub.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", product = BuildInfo.Product, version = BuildInfo.Version }));
app.MapHealthChecks("/healthz");

app.Run();

