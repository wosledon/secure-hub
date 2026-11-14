using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Models;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public sealed record LoginRequest(string UserName, string Password);
public sealed record CreateKeyRequest(string DisplayName, KeyOwnerType OwnerType, Guid OwnerId, string[]? Scopes);
public sealed record CreateProviderRequest(
    string Alias,
    string ProviderKey,
    string BaseUrl,
    string ApiKey,
    string[] Models);

public static class ManagementEndpoints
{
    public static void MapManagement(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapPost("/api/auth/logout", Logout);
        app.MapGet("/api/auth/me", Me);

        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/providers", ListProvidersAsync);
        admin.MapPost("/providers", CreateProviderAsync);
        admin.MapGet("/keys", ListKeysAsync);
        admin.MapPost("/keys", CreateKeyAsync);
        admin.MapPost("/keys/{id:guid}/revoke", RevokeKeyAsync);
        admin.MapGet("/audit", QueryAuditAsync);
        admin.MapGet("/audit/verify", VerifyAuditAsync);
        admin.MapGet("/usage/summary", UsageSummaryAsync);

        var me = app.MapGroup("/api/me");
        me.MapGet("/providers", ListMyProvidersAsync);
        me.MapPost("/providers", CreateMyProviderAsync);
        me.MapGet("/keys", ListMyKeysAsync);
        me.MapPost("/keys", CreateMyKeyAsync);
        me.MapGet("/usage", MyUsageAsync);
    }

    // ---------- auth ----------

    private static async Task LoginAsync(
        HttpContext http,
        LoginRequest request,
        SecureHubDbContext db,
        SessionStore sessions,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == request.UserName, ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await ApiError.Create(401, "auth.login_failed", localizer).ExecuteAsync(http);
            return;
        }

        var roles = user.Roles;
        var token = sessions.Create(user, roles);
        SessionCookie.Write(http, token, options.Value.Security.SessionAbsoluteHours);

        await http.Response.WriteAsJsonAsync(new
        {
            user = new { id = user.Id, user_name = user.UserName, roles, team_id = user.TeamId }
        }, ct);
    }

    private static IResult Logout(HttpContext http, SessionStore sessions)
    {
        sessions.Remove(SessionCookie.Read(http));
        SessionCookie.Clear(http);
        return Results.Ok(new { ok = true });
    }

    private static IResult Me(HttpContext http, SessionStore sessions)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        return session is null
            ? Results.Unauthorized()
            : Results.Ok(new
            {
                user = new
                {
                    id = session.UserId,
                    user_name = session.UserName,
                    roles = session.Roles,
                    team_id = session.TeamId
                }
            });
    }

    // ---------- admin ----------

    private static async Task ListProvidersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.ProviderRead);
        if (session is null) return;

        var providers = await db.Providers.AsNoTracking().OrderBy(p => p.Alias).ToListAsync(ct);
        await http.Response.WriteAsJsonAsync(providers.Select(p => new
        {
            p.Id,
            p.Alias,
            p.ProviderKey,
            p.BaseUrl,
            p.DisplayName,
            scope = p.Scope.ToString().ToLowerInvariant(),
            p.Enabled,
            p.HealthStatus
        }), ct);
    }

    private static async Task CreateProviderAsync(
        HttpContext http, CreateProviderRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.ProviderWrite);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);

        if (ModelIdReserved(request.Alias) || await db.Providers.AnyAsync(p => p.Alias == request.Alias, ct))
        {
            await ApiError.Create(400, "provider_not_allowed", localizer).ExecuteAsync(http);
            return;
        }

        var provider = new Provider
        {
            Alias = request.Alias,
            ProviderKey = request.ProviderKey,
            BaseUrl = request.BaseUrl,
            DisplayName = request.Alias,
            Scope = ProviderScope.Org,
            CreatedAt = clock.UtcNow
        };
        db.Providers.Add(provider);

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider.Id,
            CipherText = credentials.Protect(request.ApiKey),
            KeyHint = Tail(request.ApiKey),
            CreatedAt = clock.UtcNow
        });

        foreach (var model in request.Models.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderId = provider.Id,
                UpstreamModel = model,
                GroupKey = model,
                ContextWindow = 128_000,
                MaxOutputTokens = 4_096
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(
            AuditEventTypes.ProviderCreated, "user", session.UserId.ToString(), session.UserName,
            "ok", ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = provider.Id, provider.Alias }, ct);
    }

    private static async Task ListKeysAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyRead);
        if (session is null) return;

        var keys = await db.VirtualKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(keys.Select(k => new
        {
            id = k.Id,
            key_id = k.KeyId,
            prefix = k.Prefix,
            display_name = k.DisplayName,
            owner_type = k.OwnerType.ToString().ToLowerInvariant(),
            owner_id = k.OwnerId,
            is_revoked = k.IsRevoked,
            created_at = k.CreatedAt,
            expires_at = k.ExpiresAt
        }), ct);
    }

    private static async Task CreateKeyAsync(
        HttpContext http, CreateKeyRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options,
        IClock clock, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyWrite);
        if (session is null) return;

        // ownerId 缺省时挂到第一个应用上，便于冷启动
        var ownerId = request.OwnerId == Guid.Empty
            ? (await db.Apps.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct))?.Id ?? Guid.Empty
            : request.OwnerId;

        if (ownerId == Guid.Empty)
        {
            await ApiError.Create(400, "validation.required", ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var (fullKey, parsed) = VirtualKeyFormat.Generate();
        var entity = new VirtualKey
        {
            KeyId = parsed.KeyId,
            SecretHash = VirtualKeyFormat.HashSecret(parsed.Secret),
            Prefix = fullKey[..Math.Min(20, fullKey.Length)],
            DisplayName = request.DisplayName,
            OwnerType = request.OwnerType,
            OwnerId = ownerId,
            ScopesJson = System.Text.Json.JsonSerializer.Serialize(request.Scopes ?? ["chat:write", "models:read"]),
            CreatedAt = clock.UtcNow
        };

        db.VirtualKeys.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.KeyCreated, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = $"{{\"keyId\":\"{entity.KeyId}\"}}", ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = entity.Id,
            key = fullKey,
            key_id = entity.KeyId,
            display_name = entity.DisplayName
        }, ct);
    }

    private static async Task RevokeKeyAsync(
        HttpContext http, Guid id, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyWrite);
        if (session is null) return;

        var key = await db.VirtualKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        key.IsRevoked = true;
        key.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.KeyRevoked, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = $"{{\"keyId\":\"{key.KeyId}\"}}", ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static async Task QueryAuditAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.AuditRead);
        if (session is null) return;

        var from = ParseDate(http.Request.Query["from"]) ?? DateTimeOffset.UtcNow.AddDays(-7);
        var to = ParseDate(http.Request.Query["to"]) ?? DateTimeOffset.UtcNow;

        var query = db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        if (Guid.TryParse(http.Request.Query["appId"], out var appId))
        {
            query = query.Where(e => e.AppId == appId);
        }

        var eventType = http.Request.Query["eventType"].ToString();
        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        var rows = await query
            .OrderByDescending(e => e.Sequence)
            .Take(200)
            .ToListAsync(ct);

        var items = rows.Select(e => new
            {
                e.EventId,
                e.Sequence,
                e.OccurredAt,
                e.EventType,
                severity = e.Severity.ToString().ToLowerInvariant(),
                action = e.Action.ToString().ToLowerInvariant(),
                e.ActorType,
                e.ActorLabel,
                e.AppId,
                e.RequestId,
                e.ModelAlias,
                e.ProviderId,
                e.UpstreamModel,
                e.RuleId,
                e.ReasonCode,
                e.InputTokens,
                e.InputTokensOriginal,
                e.OutputTokens,
                billing_owner = e.BillingOwner.ToString().ToLowerInvariant(),
                e.ContentMissing,
                e.Hash
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, to, count = items.Count, data = items }, ct);
    }

    private static async Task VerifyAuditAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.AuditRead);
        if (session is null) return;

        var events = await db.AuditEvents.AsNoTracking()
            .OrderBy(e => e.Sequence)
            .Take(5_000)
            .ToListAsync(ct);

        var breakAt = AuditWriter.Verify(events);

        await http.Response.WriteAsJsonAsync(new
        {
            ok = breakAt is null,
            checked_count = events.Count,
            first_break_sequence = breakAt
        }, ct);
    }

    private static async Task UsageSummaryAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.UsageRead);
        if (session is null) return;

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .ToListAsync(ct);

        var grouped = rows
            .GroupBy(e => new { e.Model, e.BillingOwner })
            .Select(g => new
            {
                model = g.Key.Model,
                billing_owner = g.Key.BillingOwner.ToString().ToLowerInvariant(),
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                output_tokens = g.Sum(x => x.OutputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, data = grouped }, ct);
    }

    // ---------- me ----------

    private static async Task ListMyProvidersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var providers = await db.Providers.AsNoTracking()
            .Where(p => p.Scope == ProviderScope.User && p.OwnerUserId == session.UserId)
            .ToListAsync(ct);

        var providerIds = providers.Select(p => p.Id).ToArray();
        var offerings = await db.ProviderOfferings.AsNoTracking()
            .Where(o => providerIds.Contains(o.ProviderId))
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(providers.Select(p => new
        {
            p.Id,
            p.Alias,
            p.ProviderKey,
            p.BaseUrl,
            models = offerings.Where(o => o.ProviderId == p.Id).Select(o => o.UpstreamModel)
        }), ct);
    }

    private static async Task CreateMyProviderAsync(
        HttpContext http, CreateProviderRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);

        if (!options.Value.PersonalProviders.Enabled ||
            options.Value.PersonalProviders.BlockedUsers.Contains(session.UserId))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.ByokDisabled).ExecuteAsync(http);
            return;
        }

        if (options.Value.PersonalProviders.AllowedProviders.Length > 0 &&
            !options.Value.PersonalProviders.AllowedProviders.Contains(request.ProviderKey, StringComparer.OrdinalIgnoreCase))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.ProviderNotAllowed).ExecuteAsync(http);
            return;
        }

        if (ModelIdReserved(request.Alias) ||
            await db.Providers.AnyAsync(p => p.Alias == request.Alias, ct))
        {
            await ApiError.Create(409, "provider_not_allowed", localizer).ExecuteAsync(http);
            return;
        }

        var provider = new Provider
        {
            Alias = request.Alias,
            ProviderKey = request.ProviderKey,
            BaseUrl = request.BaseUrl,
            DisplayName = request.Alias,
            Scope = ProviderScope.User,
            OwnerUserId = session.UserId,
            CreatedAt = clock.UtcNow
        };
        db.Providers.Add(provider);

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider.Id,
            OwnerUserId = session.UserId,
            CipherText = credentials.Protect(request.ApiKey),
            KeyHint = Tail(request.ApiKey),
            CreatedAt = clock.UtcNow
        });

        foreach (var model in request.Models.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderId = provider.Id,
                UpstreamModel = model,
                GroupKey = model,
                ContextWindow = 128_000,
                MaxOutputTokens = 4_096
            });
        }

        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.ByokEnabled, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = $"{{\"provider\":\"{provider.Alias}\"}}", ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = provider.Id, provider.Alias }, ct);
    }

    private static async Task ListMyKeysAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var keys = await db.VirtualKeys.AsNoTracking()
            .Where(k => k.OwnerType == KeyOwnerType.User && k.OwnerId == session.UserId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(keys.Select(k => new
        {
            id = k.Id,
            key_id = k.KeyId,
            prefix = k.Prefix,
            display_name = k.DisplayName,
            is_revoked = k.IsRevoked,
            created_at = k.CreatedAt
        }), ct);
    }

    private static async Task CreateMyKeyAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var (fullKey, parsed) = VirtualKeyFormat.Generate();

        db.VirtualKeys.Add(new VirtualKey
        {
            KeyId = parsed.KeyId,
            SecretHash = VirtualKeyFormat.HashSecret(parsed.Secret),
            Prefix = fullKey[..Math.Min(20, fullKey.Length)],
            DisplayName = $"{session.UserName} personal",
            OwnerType = KeyOwnerType.User,
            OwnerId = session.UserId,
            ScopesJson = "[\"chat:write\",\"models:read\"]",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await http.Response.WriteAsJsonAsync(new { key = fullKey, key_id = parsed.KeyId }, ct);
    }

    private static async Task MyUsageAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.UserId == session.UserId && e.OccurredAt >= from)
            .ToListAsync(ct);

        var items = rows
            .GroupBy(e => e.Model)
            .Select(g => new
            {
                model = g.Key,
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                output_tokens = g.Sum(x => x.OutputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, data = items }, ct);
    }

    // ---------- helpers ----------

    private static SessionEntry? RequireSession(
        HttpContext http, SessionStore sessions, SecureHubOptions options)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null) return session;

        ApiError.Unauthorized(ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return null;
    }

    private static SessionEntry? RequireAdmin(
        HttpContext http, SessionStore sessions, SecureHubOptions options, Permission permission)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null && Rbac.AllowsAny(session.Roles, permission)) return session;

        var status = session is null ? 401 : 403;
        var code = session is null ? ReasonCodes.InvalidApiKey : ReasonCodes.InsufficientScope;
        ApiError.Create(status, code, ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return null;
    }

    private static bool ModelIdReserved(string alias) =>
        ModelId.ReservedProviderAliases.Contains(alias, StringComparer.OrdinalIgnoreCase);

    private static string Tail(string value) =>
        value.Length <= 4 ? value : value[^4..];

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, out var value) ? value : null;
}
