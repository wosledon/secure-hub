using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>
/// 个人工作台总览：只返回当前登录用户自己的调用、密钥、个人供应商与最近活动。
/// 所有查询都以 session.UserId 收窄，不复用管理端聚合接口。
/// </summary>
public static class PersonalOverviewEndpoints
{
    private static readonly string[] TrafficEventTypes =
    [
        AuditEventTypes.AccessDenied,
        AuditEventTypes.GuardrailBlocked,
        AuditEventTypes.GuardrailRedacted,
        AuditEventTypes.GuardrailAlert,
        AuditEventTypes.GuardrailError,
        AuditEventTypes.RouteSelected,
        AuditEventTypes.RouteDenied,
        AuditEventTypes.QuotaExceeded,
        AuditEventTypes.RateLimited,
        AuditEventTypes.CompressionApplied,
        AuditEventTypes.CompressionRolledBack
    ];

    public static void MapPersonalOverview(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/overview", GetAsync);
    }

    private static async Task GetAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var days = int.TryParse(http.Request.Query["days"], out var requested)
            ? Math.Clamp(requested, 1, 90)
            : 30;
        var from = DateTimeOffset.UtcNow.AddDays(-days);

        var usage = await db.UsageEvents.AsNoTracking()
            .Where(e => e.UserId == session.UserId && e.OccurredAt >= from)
            .Select(e => new
            {
                e.OccurredAt,
                e.Model,
                e.ProviderId,
                e.InputTokens,
                e.InputTokensOriginal,
                e.OutputTokens,
                e.CostUsd,
                e.DurationMs,
                e.ErrorCode
            })
            .ToListAsync(ct);

        var audit = await db.AuditEvents.AsNoTracking()
            .Where(e => e.ActorId == session.UserId.ToString() && e.OccurredAt >= from &&
                        TrafficEventTypes.Contains(e.EventType))
            .OrderByDescending(e => e.Sequence)
            .Select(e => new
            {
                e.EventId,
                e.EventType,
                e.OccurredAt,
                e.Action,
                e.Severity,
                e.ReasonCode,
                e.ModelAlias
            })
            .ToListAsync(ct);

        var blockedEvents = audit.Where(e => e.Action == GuardrailAction.Block).ToList();

        var providerAliases = await db.Providers.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Alias, ct);

        var providers = await db.Providers.AsNoTracking()
            .Where(p => p.Scope == ProviderScope.User && p.OwnerUserId == session.UserId)
            .Select(p => new { p.Id, p.Alias, p.Enabled, p.HealthStatus })
            .ToListAsync(ct);

        var providerIds = providers.Select(p => p.Id).ToArray();
        var availableModels = providerIds.Length == 0
            ? 0
            : await db.ProviderOfferings.AsNoTracking()
                .Where(o => providerIds.Contains(o.ProviderId) && o.Enabled)
                .Select(o => o.UpstreamModel)
                .Distinct()
                .CountAsync(ct);

        var keys = await db.VirtualKeys.AsNoTracking()
            .Where(k => k.OwnerType == KeyOwnerType.User && k.OwnerId == session.UserId)
            .Select(k => new { k.IsRevoked, k.ExpiresAt })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var activeKeys = keys.Count(k => !k.IsRevoked && (k.ExpiresAt is null || k.ExpiresAt > now));

        var durations = usage.Select(e => e.DurationMs).OrderBy(value => value).ToArray();
        var requests = usage.Count;
        var errors = usage.Count(e => e.ErrorCode is not null);
        var inputTokens = usage.Sum(e => (long)e.InputTokens);
        var outputTokens = usage.Sum(e => (long)e.OutputTokens);

        var usageByDay = usage
            .GroupBy(e => e.OccurredAt.UtcDateTime.Date)
            .ToDictionary(
                g => g.Key,
                g => (
                    Requests: g.Count(),
                    Cost: g.Sum(x => x.CostUsd),
                    Input: g.Sum(x => (long)x.InputTokens),
                    Output: g.Sum(x => (long)x.OutputTokens)
                ));

        var blockedByDay = blockedEvents
            .GroupBy(e => e.OccurredAt.UtcDateTime.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
        var byDay = Enumerable.Range(0, days)
            .Select(index => today.AddDays(-(days - 1 - index)))
            .Select(date => new
            {
                date = date.ToString("yyyy-MM-dd"),
                requests = usageByDay.TryGetValue(date, out var totals) ? totals.Requests : 0,
                input_tokens = usageByDay.TryGetValue(date, out var input) ? input.Input : 0,
                output_tokens = usageByDay.TryGetValue(date, out var output) ? output.Output : 0,
                cost_usd = usageByDay.TryGetValue(date, out var cost) ? cost.Cost : 0,
                blocked = blockedByDay.GetValueOrDefault(date)
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new
        {
            from,
            days,
            totals = new
            {
                requests,
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                tokens = inputTokens + outputTokens,
                cost_usd = usage.Sum(e => e.CostUsd),
                compression_saved_tokens = usage.Sum(e => (long)Math.Max(0, e.InputTokensOriginal - e.InputTokens)),
                blocked = blockedEvents.Count,
                errors,
                success_rate = requests == 0 ? 100 : Math.Round((requests - errors) * 100.0 / requests, 2),
                avg_ms = durations.Length == 0 ? 0 : Math.Round(durations.Average(), 1),
                p95_ms = Percentile(durations, 0.95)
            },
            by_day = byDay,
            by_model = usage
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
                .ToList(),
            by_provider = usage
                .GroupBy(e => e.ProviderId)
                .Select(g => new
                {
                    provider = g.Key is { } providerId
                        ? providerAliases.GetValueOrDefault(providerId, "unknown")
                        : "unknown",
                    requests = g.Count(),
                    cost_usd = g.Sum(x => x.CostUsd)
                })
                .OrderByDescending(x => x.requests)
                .ToList(),
            keys = new
            {
                total = keys.Count,
                active = activeKeys
            },
            providers = new
            {
                total = providers.Count,
                enabled = providers.Count(p => p.Enabled),
                healthy = providers.Count(p => p.Enabled && p.HealthStatus == "healthy"),
                aliases = providers.Select(p => p.Alias).ToList()
            },
            models = new
            {
                used = usage.Select(e => e.Model).Distinct().Count(),
                available = availableModels
            },
            recent = audit
                .Take(6)
                .Select(e => new
                {
                    event_id = e.EventId,
                    event_type = e.EventType,
                    occurred_at = e.OccurredAt,
                    action = e.Action.ToString().ToLowerInvariant(),
                    severity = e.Severity.ToString().ToLowerInvariant(),
                    reason_code = e.ReasonCode,
                    model_alias = e.ModelAlias
                })
                .ToList()
        }, ct);
    }

    private static double Percentile(int[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
