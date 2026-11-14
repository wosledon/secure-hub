using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public sealed record BaselineCheck(string Id, string Severity, string Message, bool Ok);

/// <summary>系统：安全基线自检与生效配置。</summary>
public static class SystemEndpoints
{
    public static void MapSystem(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/system");
        group.MapGet("/baseline", BaselineAsync);
        group.MapGet("/settings", SettingsAsync);
    }

    private static async Task BaselineAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.SystemWrite) is null) return;

        var settings = options.Value;
        var checks = new List<BaselineCheck>();

        var recent = await db.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .Take(5000)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
        var breakAt = AuditWriter.Verify(recent);
        checks.Add(Check("audit_chain", breakAt is null ? "ok" : "critical",
            breakAt is null ? "Audit hash chain verified" : $"Audit chain broken at sequence {breakAt}",
            breakAt is null));

        var passwordFile = Path.Combine(settings.Security.DataDirectory, "initial-admin-password.txt");
        checks.Add(Check("bootstrap_password_removed", File.Exists(passwordFile) ? "warning" : "ok",
            File.Exists(passwordFile)
                ? "Initial admin password file still exists; delete it after first sign-in"
                : "Initial admin password file removed",
            !File.Exists(passwordFile)));

        var keyDirectory = Path.Combine(settings.Security.DataDirectory, "keys");
        var keyCount = Directory.Exists(keyDirectory)
            ? Directory.GetFiles(keyDirectory, "*.xml").Length
            : 0;
        checks.Add(Check("data_protection_keys", keyCount > 0 ? "ok" : "critical",
            keyCount > 0 ? $"{keyCount} persisted data-protection key(s)" : "No persisted data-protection keys",
            keyCount > 0));

        var contentKey = Path.Combine(settings.Security.DataDirectory, "content.key");
        checks.Add(Check("content_key", File.Exists(contentKey) ? "ok" : "critical",
            File.Exists(contentKey) ? "Content encryption key present" : "Content encryption key missing",
            File.Exists(contentKey)));

        checks.Add(Check("https", settings.Security.RequireHttps ? "ok" : "warning",
            settings.Security.RequireHttps ? "HTTPS required" : "HTTPS is not enforced; bind to loopback or place behind TLS",
            settings.Security.RequireHttps));

        checks.Add(Check("content_retention", settings.Content.PersistContent ? "ok" : "warning",
            settings.Content.PersistContent
                ? $"Content retention enabled: raw {settings.Content.RawRetentionDays}d / redacted {settings.Content.RedactedRetentionDays}d"
                : "Content persistence is disabled globally (employee traffic is still recorded)",
            true));

        var providers = await db.Providers.AsNoTracking().CountAsync(ct);
        var credentials = await db.ProviderCredentials.AsNoTracking().CountAsync(c => c.RetiredAt == null, ct);
        checks.Add(Check("provider_credentials", credentials >= providers ? "ok" : "warning",
            $"{credentials} active credential(s) for {providers} provider(s)",
            credentials >= providers));

        checks.Add(Check("byok", "info",
            settings.PersonalProviders.Enabled
                ? $"BYOK enabled; {settings.PersonalProviders.AllowedProviders.Length} provider allowlist entries"
                : "BYOK disabled",
            true));

        checks.Add(Check("database", "info",
            $"{settings.Database.Provider} · auto-migrate {settings.Database.AutoMigrate}",
            true));

        await http.Response.WriteAsJsonAsync(new
        {
            checked_at = clock.UtcNow,
            healthy = checks.All(check => check.Ok),
            checks
        }, ct);
    }

    private static async Task SettingsAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.SystemWrite) is null) return;

        var settings = options.Value;
        var providerCount = await db.Providers.CountAsync(ct);
        var blockedUsers = settings.PersonalProviders.BlockedUsers.Length;
        var allowedProviders = settings.PersonalProviders.AllowedProviders;

        await http.Response.WriteAsJsonAsync(new
        {
            security = new
            {
                require_https = settings.Security.RequireHttps,
                session_hours = settings.Security.SessionAbsoluteHours,
                request_body_limit = settings.Security.RequestBodyLimitBytes,
                admin_bind_address = settings.Security.AdminBindAddress
            },
            database = new
            {
                provider = settings.Database.Provider,
                auto_migrate = settings.Database.AutoMigrate
            },
            content = new
            {
                persist = settings.Content.PersistContent,
                raw_days = settings.Content.RawRetentionDays,
                redacted_days = settings.Content.RedactedRetentionDays,
                compressed_days = settings.Content.CompressedRetentionDays,
                max_bytes = settings.Content.MaxContentBytes
            },
            guardrails = new
            {
                on_error = settings.Guardrails.OnError,
                redaction_mode = settings.Guardrails.RedactionMode,
                token_retention_hours = settings.Guardrails.TokenRetentionHours
            },
            personal_providers = new
            {
                enabled = settings.PersonalProviders.Enabled,
                allowed_providers = allowedProviders,
                blocked_users = blockedUsers
            },
            providers = new { total = providerCount },
            note = "Settings are configuration-only; change appsettings or environment variables and restart."
        }, ct);
    }

    private static BaselineCheck Check(string id, string severity, string message, bool ok) =>
        new(id, severity, message, ok);
}
