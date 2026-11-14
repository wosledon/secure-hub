using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SecureHub.Api.Auth;
using SecureHub.Api.Endpoints;
using SecureHub.Api.Services;
using SecureHub.Core;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Options;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Data;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // 静态资源随程序输出，避免运行时依赖仓库目录结构
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

var hubOptions = builder.Configuration
    .GetSection(SecureHubOptions.SectionName)
    .Get<SecureHubOptions>() ?? new SecureHubOptions();

builder.Services.Configure<SecureHubOptions>(
    builder.Configuration.GetSection(SecureHubOptions.SectionName));

builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddSecureHubDbContext(o =>
{
    if (string.Equals(hubOptions.Database.Provider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        o.UseNpgsql(hubOptions.Database.ConnectionString);
    }
    else
    {
        o.UseSqlite(hubOptions.Database.ConnectionString);
    }
});

Directory.CreateDirectory(hubOptions.Security.DataDirectory);
Directory.CreateDirectory(Path.Combine(hubOptions.Security.DataDirectory, "keys"));

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(hubOptions.Security.DataDirectory, "keys")))
    .SetApplicationName("SecureHub");

builder.Services.AddHttpClient("upstream", c =>
{
    c.Timeout = TimeSpan.FromMinutes(5);
    c.DefaultRequestHeaders.Add("User-Agent", "SecureHub/0.1");
});

builder.Services.AddSingleton<SessionStore>();
builder.Services.AddScoped<ApiKeyAuthenticator>();
builder.Services.AddSingleton<ContentCipher>();
builder.Services.AddSingleton<AuditWriter>();
builder.Services.AddSingleton<ProviderCredentialStore>();
builder.Services.AddScoped<ContentStore>();
builder.Services.AddScoped<RouteResolver>();
builder.Services.AddScoped<GuardrailPipeline>(_ => new GuardrailPipeline(
    [new PiiDetector(), new SecretDetector()],
    new GuardrailOptionsLike(
        hubOptions.Guardrails.TotalBudgetMs,
        hubOptions.Guardrails.PerDetectorBudgetMs,
        hubOptions.Guardrails.OnError)));

builder.Services.AddHealthChecks();

var app = builder.Build();

if (hubOptions.Database.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    await bootstrapper.InitializeAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    product = BuildInfo.Product,
    version = BuildInfo.Version
}));
app.MapHealthChecks("/healthz");

app.MapDataPlane();
app.MapManagement();

// SPA 回退：/admin、/me 等前端路由返回 index.html；
// /api 与 /v1 下的未知路径保持 404，避免把 JSON 调用者导到 HTML。
app.MapFallback(async context =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();

