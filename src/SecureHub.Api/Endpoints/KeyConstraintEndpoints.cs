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

public sealed record UpdateKeyConstraintsRequest(
    string[]? AllowedModels,
    string[]? AllowedProviders,
    string[]? AllowedIpCidrs,
    int? Rpm,
    int? Tpm,
    int? MaxConcurrency,
    DateTimeOffset? ExpiresAt);

/// <summary>密钥高级约束、吊销与个人密钥轮换。</summary>
public static class KeyConstraintEndpoints
{
    public static void MapKeyConstraints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/admin/keys/{id:guid}/constraints", (Guid id, HttpContext http, UpdateKeyConstraintsRequest request,
            SessionStore sessions, SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            UpdateAsync(id, http, request, sessions, db, audit, options, clock, ct, adminScope: true));

        var me = app.MapGroup("/api/me/keys");
        me.MapPut("/{id:guid}/constraints", (Guid id, HttpContext http, UpdateKeyConstraintsRequest request,
            SessionStore sessions, SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            UpdateAsync(id, http, request, sessions, db, audit, options, clock, ct, adminScope: false));
        me.MapPost("/{id:guid}/revoke", RevokeMyKeyAsync);
        me.MapPost("/{id:guid}/rotate", RotateMyKeyAsync);
    }

    private static async Task UpdateAsync(
        Guid id, HttpContext http, UpdateKeyConstraintsRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options,
        IClock clock, CancellationToken ct, bool adminScope)
    {
        var session = adminScope
            ? EndpointAuth.Require(http, sessions, options.Value, Permission.KeyWrite)
            : EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var key = await db.VirtualKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null || (!adminScope && (key.OwnerType != KeyOwnerType.User || key.OwnerId != session.UserId)))
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        if (request.Rpm is <= 0 || request.Tpm is <= 0 || request.MaxConcurrency is <= 0)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= clock.UtcNow)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var constraints = new KeyConstraints(
            Clean(request.AllowedModels),
            Clean(request.AllowedProviders),
            Clean(request.AllowedIpCidrs),
            request.Rpm,
            request.Tpm,
            request.MaxConcurrency);

        var invalidCidr = constraints.AllowedIpCidrs.FirstOrDefault(cidr => !IsCidr(cidr));
        if (invalidCidr is not null)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        key.ConstraintsJson = constraints.ToJson();
        if (request.ExpiresAt is { } newExpiry) key.ExpiresAt = newExpiry;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(AuditEventTypes.KeyConstraintsUpdated, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                key_id = key.KeyId,
                rpm = constraints.RequestsPerMinute,
                tpm = constraints.TokensPerMinute,
                max_concurrency = constraints.MaxConcurrency,
                models = constraints.AllowedModels.Count
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            ok = true,
            constraints = new
            {
                allowedModels = constraints.AllowedModels,
                allowedProviders = constraints.AllowedProviders,
                allowedIpCidrs = constraints.AllowedIpCidrs,
                rpm = constraints.RequestsPerMinute,
                tpm = constraints.TokensPerMinute,
                maxConcurrency = constraints.MaxConcurrency
            },
            expires_at = key.ExpiresAt
        }, ct);
    }

    private static async Task RevokeMyKeyAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var key = await db.VirtualKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.OwnerType == KeyOwnerType.User && k.OwnerId == session.UserId, ct);
        if (key is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        key.IsRevoked = true;
        key.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.KeyRevoked, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { key_id = key.KeyId }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static async Task RotateMyKeyAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var key = await db.VirtualKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.OwnerType == KeyOwnerType.User && k.OwnerId == session.UserId, ct);
        if (key is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        key.IsRevoked = true;
        key.RevokedAt = clock.UtcNow;

        var (fullKey, parsed) = VirtualKeyFormat.Generate();
        var replacement = new VirtualKey
        {
            KeyId = parsed.KeyId,
            SecretHash = VirtualKeyFormat.HashSecret(parsed.Secret),
            Prefix = fullKey[..Math.Min(20, fullKey.Length)],
            DisplayName = key.DisplayName,
            OwnerType = KeyOwnerType.User,
            OwnerId = key.OwnerId,
            AppName = key.AppName,
            TeamId = key.TeamId,
            ScopesJson = key.ScopesJson,
            ConstraintsJson = key.ConstraintsJson,
            ExpiresAt = key.ExpiresAt,
            RotatedFromId = key.Id,
            CreatedAt = clock.UtcNow
        };
        db.VirtualKeys.Add(replacement);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(AuditEventTypes.KeyRotated, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                from = key.KeyId,
                to = replacement.KeyId
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            key = fullKey,
            key_id = replacement.KeyId,
            replaced_key_id = key.KeyId
        }, ct);
    }

    private static string[] Clean(string[]? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsCidr(string value)
    {
        var parts = value.Split('/', 2);
        if (!System.Net.IPAddress.TryParse(parts[0], out var address)) return false;
        if (parts.Length == 1) return true;

        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= max;
    }
}
