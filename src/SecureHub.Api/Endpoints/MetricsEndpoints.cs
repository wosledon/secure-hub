using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>数据面可观测性：延迟分位、错误率、按模型/供应商/小时分布。</summary>
public static class MetricsEndpoints
{
    public static void MapMetrics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/metrics", GetAsync);
    }

    private static async Task GetAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.UsageRead) is null) return;

        var days = int.TryParse(http.Request.Query["days"], out var requested) ? Math.Clamp(requested, 1, 90) : 7;
        var from = DateTimeOffset.UtcNow.AddDays(-days);

        var events = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .Select(e => new
            {
                e.ProviderId,
                e.Model,
                e.DurationMs,
                e.ErrorCode,
                e.OccurredAt
            })
            .ToListAsync(ct);

        var providers = await db.Providers.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Alias, ct);
        var durations = events.Select(e => e.DurationMs).OrderBy(value => value).ToArray();
        var errors = events.Count(e => e.ErrorCode is not null);

        var blocked = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from &&
                        (e.EventType == AuditEventTypes.GuardrailBlocked ||
                         e.EventType == AuditEventTypes.RateLimited ||
                         e.EventType == AuditEventTypes.AuthFailed))
            .GroupBy(e => e.EventType)
            .Select(g => new { event_type = g.Key, count = g.Count() })
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(new
        {
            from,
            days,
            summary = new
            {
                requests = events.Count,
                errors,
                success_rate = events.Count == 0 ? 100 : Math.Round((events.Count - errors) * 100.0 / events.Count, 2),
                avg_ms = durations.Length == 0 ? 0 : Math.Round(durations.Average(), 1),
                p50_ms = Percentile(durations, 0.50),
                p95_ms = Percentile(durations, 0.95),
                p99_ms = Percentile(durations, 0.99)
            },
            security = blocked,
            by_model = events
                .GroupBy(e => e.Model)
                .Select(g =>
                {
                    var values = g.Select(x => x.DurationMs).OrderBy(value => value).ToArray();
                    return new
                    {
                        model = g.Key,
                        requests = g.Count(),
                        errors = g.Count(x => x.ErrorCode is not null),
                        avg_ms = values.Length == 0 ? 0 : Math.Round(values.Average(), 1),
                        p95_ms = Percentile(values, 0.95)
                    };
                })
                .OrderByDescending(x => x.requests)
                .Take(25),
            by_provider = events
                .GroupBy(e => e.ProviderId)
                .Select(g =>
                {
                    var values = g.Select(x => x.DurationMs).OrderBy(value => value).ToArray();
                    return new
                    {
                        provider_id = g.Key,
                        provider = g.Key is { } providerId ? providers.GetValueOrDefault(providerId) ?? "unknown" : "unassigned",
                        requests = g.Count(),
                        errors = g.Count(x => x.ErrorCode is not null),
                        avg_ms = values.Length == 0 ? 0 : Math.Round(values.Average(), 1),
                        p95_ms = Percentile(values, 0.95)
                    };
                })
                .OrderByDescending(x => x.requests),
            by_hour = Enumerable.Range(0, 24).Select(hour => new
            {
                hour,
                requests = events.Count(e => e.OccurredAt.UtcDateTime.Hour == hour),
                errors = events.Count(e => e.OccurredAt.UtcDateTime.Hour == hour && e.ErrorCode is not null)
            })
        }, ct);
    }

    private static int Percentile(int[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
