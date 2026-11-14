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

public sealed record UpsertEntitlementRequest(
    Guid? Id,
    int SubjectType,
    Guid? SubjectId,
    Guid ProviderId,
    string[]? Models,
    long? TokenQuota,
    decimal? CostQuota,
    DateTimeOffset? ExpiresAt);

/// <summary>企业授权：把供应商/模型分配给全体、团队或个人。</summary>
public static class EntitlementEndpoints
{
    public static void MapEntitlements(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/entitlements");
        group.MapGet("", ListAsync);
        group.MapPost("", UpsertAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static async Task ListAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.ProviderRead) is null) return;

        var rows = await db.Entitlements.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var providers = await db.Providers.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Alias, ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        await http.Response.WriteAsJsonAsync(rows.Select(e => new
        {
            e.Id,
            subject_type = e.SubjectType.ToString().ToLowerInvariant(),
            e.SubjectId,
            subject_label = e.SubjectType switch
            {
                SubjectType.Team => e.SubjectId is { } teamId ? teams.GetValueOrDefault(teamId) : null,
                SubjectType.User => e.SubjectId is { } userId ? users.GetValueOrDefault(userId) : null,
                _ => null
            },
            e.ProviderId,
            provider_alias = providers.GetValueOrDefault(e.ProviderId),
            models = ParseModels(e.ModelsJson),
            e.TokenQuota,
            e.CostQuota,
            e.TokensUsed,
            e.ExpiresAt,
            e.CreatedAt
        }), ct);
    }

    private static async Task UpsertAsync(
        HttpContext http, UpsertEntitlementRequest request, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.ProviderWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        if (!Enum.IsDefined(typeof(SubjectType), request.SubjectType))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var subjectType = (SubjectType)request.SubjectType;
        if (subjectType != SubjectType.All && request.SubjectId is null)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (subjectType == SubjectType.Team &&
            !await db.Teams.AnyAsync(t => t.Id == request.SubjectId, ct))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (subjectType == SubjectType.User &&
            !await db.Users.AnyAsync(u => u.Id == request.SubjectId, ct))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProviderId, ct);
        if (provider is null)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        var models = (request.Models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (models.Length > 0)
        {
            var available = await db.ProviderOfferings.AsNoTracking()
                .Where(o => o.ProviderId == provider.Id)
                .Select(o => o.UpstreamModel)
                .ToListAsync(ct);
            var unknown = models.Where(model => !available.Contains(model, StringComparer.Ordinal)).ToArray();
            if (unknown.Length > 0)
            {
                await ApiError.Create(400, "validation.catalog_model_not_found", localizer, candidates: unknown).ExecuteAsync(http);
                return;
            }
        }

        if (request.TokenQuota is <= 0 || request.CostQuota is <= 0)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        Entitlement? entitlement = null;
        if (request.Id is { } id)
        {
            entitlement = await db.Entitlements.FirstOrDefaultAsync(e => e.Id == id, ct);
        }

        entitlement ??= new Entitlement
        {
            SubjectType = subjectType,
            SubjectId = subjectType == SubjectType.All ? null : request.SubjectId,
            ProviderId = provider.Id,
            CreatedBy = session.UserId,
            CreatedAt = clock.UtcNow
        };

        entitlement.SubjectType = subjectType;
        entitlement.SubjectId = subjectType == SubjectType.All ? null : request.SubjectId;
        entitlement.ProviderId = provider.Id;
        entitlement.ModelsJson = models.Length == 0 ? null : JsonSerializer.Serialize(models);
        entitlement.TokenQuota = request.TokenQuota;
        entitlement.CostQuota = request.CostQuota;
        entitlement.ExpiresAt = request.ExpiresAt;

        db.Entitlements.Add(entitlement);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(AuditEventTypes.EntitlementUpdated, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                provider = provider.Alias,
                subject = subjectType.ToString().ToLowerInvariant(),
                models = models.Length
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = entitlement.Id }, ct);
    }

    private static async Task DeleteAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.ProviderWrite);
        if (session is null) return;

        var entitlement = await db.Entitlements.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entitlement is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        db.Entitlements.Remove(entitlement);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.EntitlementRevoked, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                provider_id = entitlement.ProviderId,
                subject = entitlement.SubjectType.ToString().ToLowerInvariant()
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static string[] ParseModels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
