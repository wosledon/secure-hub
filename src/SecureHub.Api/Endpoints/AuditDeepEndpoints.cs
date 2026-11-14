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

/// <summary>审计深度：事件详情、请求链路、正文解密与到期内容清理。</summary>
public static class AuditDeepEndpoints
{
    public static void MapAuditDeep(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/audit");
        group.MapGet("/{eventId:guid}", GetEventAsync);
        group.MapGet("/request/{requestId}", GetRequestTraceAsync);
        group.MapGet("/content/{contentId:guid}", GetContentAsync);
        group.MapPost("/retention/purge", PurgeAsync);
    }

    private static async Task GetEventAsync(
        Guid eventId, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.AuditRead) is null) return;

        var evt = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (evt is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var disputes = await db.AuditEventDisputes.AsNoTracking()
            .Where(d => d.EventId == eventId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var content = string.IsNullOrEmpty(evt.RequestId)
            ? []
            : await db.ContentBlobs.AsNoTracking()
                .Where(b => b.RequestId == evt.RequestId)
                .OrderBy(b => b.Direction)
                .ThenBy(b => b.Variant)
                .Select(b => new
                {
                    content_id = b.ContentId,
                    direction = b.Direction.ToString().ToLowerInvariant(),
                    variant = b.Variant.ToString().ToLowerInvariant(),
                    size_bytes = b.SizeBytes,
                    truncated = b.Truncated,
                    created_at = b.CreatedAt,
                    expires_at = b.ExpiresAt,
                    expired = b.ExpiresAt <= DateTimeOffset.UtcNow
                })
                .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(new
        {
            @event = evt,
            disputes = disputes.Select(d => new
            {
                d.Id,
                d.EventId,
                dispute_type = d.DisputeType,
                d.Note,
                d.CreatedBy,
                d.CreatedAt,
                d.ResolvedAt
            }),
            content
        }, ct);
    }

    private static async Task GetRequestTraceAsync(
        string requestId, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.AuditRead) is null) return;

        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.RequestId == requestId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        var content = await db.ContentBlobs.AsNoTracking()
            .Where(b => b.RequestId == requestId)
            .OrderBy(b => b.Direction)
            .ThenBy(b => b.Variant)
            .Select(b => new
            {
                content_id = b.ContentId,
                direction = b.Direction.ToString().ToLowerInvariant(),
                variant = b.Variant.ToString().ToLowerInvariant(),
                size_bytes = b.SizeBytes,
                truncated = b.Truncated,
                created_at = b.CreatedAt,
                expires_at = b.ExpiresAt
            })
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(new { request_id = requestId, events, content }, ct);
    }

    private static async Task GetContentAsync(
        Guid contentId, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        ContentCipher cipher, AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.ContentRead);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var blob = await db.ContentBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.ContentId == contentId, ct);
        if (blob is null)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        if (blob.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await ApiError.Create(410, ReasonCodes.ContentExpired, localizer).ExecuteAsync(http);
            return;
        }

        string text;
        try
        {
            var aad = ContentCipher.BuildAad(blob.RequestId, blob.Direction.ToString(), blob.Variant.ToString());
            text = cipher.Open(blob.CipherText, blob.Nonce, blob.Tag, aad);
        }
        catch
        {
            await ApiError.Create(500, ReasonCodes.ContentNotFound, localizer).ExecuteAsync(http);
            return;
        }

        await audit.AppendAsync(AuditEventTypes.ContentViewed, "user", session.UserId.ToString(), session.UserName,
            "ok", severity: AuditSeverity.Warning, configure: e =>
            {
                e.RequestId = blob.RequestId;
                e.AppName = blob.AppName;
                e.Detail = JsonSerializer.Serialize(new
                {
                    content_id = blob.ContentId,
                    direction = blob.Direction.ToString().ToLowerInvariant(),
                    variant = blob.Variant.ToString().ToLowerInvariant()
                });
            }, ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            content_id = blob.ContentId,
            request_id = blob.RequestId,
            direction = blob.Direction.ToString().ToLowerInvariant(),
            variant = blob.Variant.ToString().ToLowerInvariant(),
            text,
            truncated = blob.Truncated,
            size_bytes = blob.SizeBytes,
            created_at = blob.CreatedAt,
            expires_at = blob.ExpiresAt
        }, ct);
    }

    private static async Task PurgeAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db, AuditWriter audit,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.SystemWrite);
        if (session is null) return;

        var now = clock.UtcNow;
        var expiredBlobs = await db.ContentBlobs.Where(b => b.ExpiresAt <= now).ToListAsync(ct);
        var expiredTokens = await db.RedactionTokens.Where(t => t.ExpiresAt <= now).ToListAsync(ct);

        var bytes = expiredBlobs.Sum(b => (long)b.SizeBytes);
        db.ContentBlobs.RemoveRange(expiredBlobs);
        db.RedactionTokens.RemoveRange(expiredTokens);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(AuditEventTypes.ContentPurged, "user", session.UserId.ToString(), session.UserName,
            "ok", severity: AuditSeverity.Warning, configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                content_blobs = expiredBlobs.Count,
                redaction_tokens = expiredTokens.Count,
                bytes
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            ok = true,
            content_blobs = expiredBlobs.Count,
            redaction_tokens = expiredTokens.Count,
            bytes
        }, ct);
    }
}
