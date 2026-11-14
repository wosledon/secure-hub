using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>成本与容量分析：按天/团队/应用/供应商拆分，压缩收益与存储占用。</summary>
public static class UsageInsightsEndpoints
{
    public static void MapUsageInsights(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/usage/breakdown", BreakdownAsync);
        app.MapGet("/api/admin/capacity", CapacityAsync);
    }

    private static async Task BreakdownAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.UsageRead) is null) return;

        var days = int.TryParse(http.Request.Query["days"], out var requested) ? Math.Clamp(requested, 1, 90) : 7;
        var from = DateTimeOffset.UtcNow.AddDays(-days);

        var events = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .ToListAsync(ct);

        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var providers = await db.Providers.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Alias, ct);

        var totals = new
        {
            requests = events.Count,
            input_tokens = events.Sum(e => (long)e.InputTokens),
            input_tokens_original = events.Sum(e => (long)e.InputTokensOriginal),
            output_tokens = events.Sum(e => (long)e.OutputTokens),
            cost_usd = events.Sum(e => e.CostUsd),
            compression_saved_tokens = events.Sum(e => (long)Math.Max(0, e.InputTokensOriginal - e.InputTokens)),
            org_requests = events.Count(e => e.BillingOwner == Core.Domain.BillingOwner.Org),
            user_requests = events.Count(e => e.BillingOwner == Core.Domain.BillingOwner.User)
        };

        await http.Response.WriteAsJsonAsync(new
        {
            from,
            days,
            totals,
            by_day = events
                .GroupBy(e => e.OccurredAt.UtcDateTime.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    requests = g.Count(),
                    input_tokens = g.Sum(x => (long)x.InputTokens),
                    output_tokens = g.Sum(x => (long)x.OutputTokens),
                    cost_usd = g.Sum(x => x.CostUsd),
                    compression_saved_tokens = g.Sum(x => (long)Math.Max(0, x.InputTokensOriginal - x.InputTokens))
                }),
            by_team = events
                .GroupBy(e => e.TeamId)
                .Select(g => new
                {
                    team_id = g.Key,
                    name = g.Key is { } teamId ? teams.GetValueOrDefault(teamId) ?? "unknown" : "unassigned",
                    requests = g.Count(),
                    input_tokens = g.Sum(x => (long)x.InputTokens),
                    output_tokens = g.Sum(x => (long)x.OutputTokens),
                    cost_usd = g.Sum(x => x.CostUsd)
                })
                .OrderByDescending(x => x.requests),
            by_app = events
                .GroupBy(e => e.AppName)
                .Select(g => new
                {
                    name = string.IsNullOrWhiteSpace(g.Key) ? "unassigned" : g.Key,
                    requests = g.Count(),
                    input_tokens = g.Sum(x => (long)x.InputTokens),
                    output_tokens = g.Sum(x => (long)x.OutputTokens),
                    cost_usd = g.Sum(x => x.CostUsd)
                })
                .OrderByDescending(x => x.requests),
            by_provider = events
                .GroupBy(e => e.ProviderId)
                .Select(g => new
                {
                    provider_id = g.Key,
                    name = g.Key is { } providerId ? providers.GetValueOrDefault(providerId) ?? "unknown" : "unassigned",
                    requests = g.Count(),
                    input_tokens = g.Sum(x => (long)x.InputTokens),
                    output_tokens = g.Sum(x => (long)x.OutputTokens),
                    cost_usd = g.Sum(x => x.CostUsd)
                })
                .OrderByDescending(x => x.requests),
            by_model = events
                .GroupBy(e => e.Model)
                .Select(g => new
                {
                    model = g.Key,
                    requests = g.Count(),
                    input_tokens = g.Sum(x => (long)x.InputTokens),
                    output_tokens = g.Sum(x => (long)x.OutputTokens),
                    cost_usd = g.Sum(x => x.CostUsd)
                })
                .OrderByDescending(x => x.requests)
        }, ct);
    }

    private static async Task CapacityAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.UsageRead) is null) return;

        var now = DateTimeOffset.UtcNow;
        var blobs = await db.ContentBlobs.AsNoTracking()
            .Select(b => new { b.Variant, b.Direction, b.SizeBytes, b.ExpiresAt })
            .ToListAsync(ct);
        var keys = await db.VirtualKeys.AsNoTracking()
            .Select(k => new { k.IsRevoked, k.ExpiresAt })
            .ToListAsync(ct);
        var tokens = await db.RedactionTokens.AsNoTracking()
            .Select(t => new { t.ExpiresAt })
            .ToListAsync(ct);

        var retention = options.Value.Content;
        await http.Response.WriteAsJsonAsync(new
        {
            retention = new
            {
                raw_days = retention.RawRetentionDays,
                redacted_days = retention.RedactedRetentionDays,
                compressed_days = retention.CompressedRetentionDays,
                max_content_bytes = retention.MaxContentBytes,
                persist_content = retention.PersistContent
            },
            content = new
            {
                blobs = blobs.Count,
                bytes = blobs.Sum(b => (long)b.SizeBytes),
                expired_blobs = blobs.Count(b => b.ExpiresAt <= now),
                expired_bytes = blobs.Where(b => b.ExpiresAt <= now).Sum(b => (long)b.SizeBytes),
                by_variant = blobs
                    .GroupBy(b => b.Variant)
                    .Select(g => new
                    {
                        variant = g.Key.ToString().ToLowerInvariant(),
                        blobs = g.Count(),
                        bytes = g.Sum(x => (long)x.SizeBytes)
                    }),
                by_direction = blobs
                    .GroupBy(b => b.Direction)
                    .Select(g => new
                    {
                        direction = g.Key.ToString().ToLowerInvariant(),
                        blobs = g.Count(),
                        bytes = g.Sum(x => (long)x.SizeBytes)
                    })
            },
            keys = new
            {
                total = keys.Count,
                active = keys.Count(k => !k.IsRevoked && (k.ExpiresAt is null || k.ExpiresAt > now)),
                revoked = keys.Count(k => k.IsRevoked),
                expired = keys.Count(k => !k.IsRevoked && k.ExpiresAt is not null && k.ExpiresAt <= now)
            },
            redaction_tokens = new
            {
                total = tokens.Count,
                expired = tokens.Count(t => t.ExpiresAt <= now)
            }
        }, ct);
    }
}
