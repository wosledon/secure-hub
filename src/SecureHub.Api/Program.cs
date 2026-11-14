using Microsoft.EntityFrameworkCore;
using SecureHub.Core;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Options;
using SecureHub.Data;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHealthChecks();

var app = builder.Build();

if (hubOptions.Database.AutoMigrate)
{
    Directory.CreateDirectory(hubOptions.Security.DataDirectory);
    using var scope = app.Services.CreateScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    await bootstrapper.InitializeAsync();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    product = BuildInfo.Product,
    version = BuildInfo.Version
}));
app.MapHealthChecks("/healthz");

app.Run();

