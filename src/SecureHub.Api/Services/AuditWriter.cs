using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>
/// 审计写入：单写队列 + 链式哈希（PRD §7.5.3）。
/// 单例注册，保证 sequence 单调；每次写入创建独立 scope 取 DbContext。
/// </summary>
public sealed class AuditWriter
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<AuditWriter> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _sequence;
    private string _lastHash = new('0', 64);
    private bool _initialized;

    public AuditWriter(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<AuditWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AuditEvent> AppendAsync(AuditEvent evt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();

            if (!_initialized)
            {
                var last = await db.AuditEvents
                    .AsNoTracking()
                    .OrderByDescending(e => e.Sequence)
                    .FirstOrDefaultAsync(ct);

                if (last is not null)
                {
                    _sequence = last.Sequence;
                    _lastHash = last.Hash;
                }

                _initialized = true;
            }

            evt.OccurredAt = _clock.UtcNow;
            evt.Sequence = ++_sequence;
            evt.PrevHash = _lastHash;
            evt.Hash = ComputeHash(evt.PrevHash, Canonicalize(evt));

            db.AuditEvents.Add(evt);
            await db.SaveChangesAsync(ct);

            _lastHash = evt.Hash;
            return evt;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(
        string eventType,
        string actorType,
        string actorId,
        string actorLabel,
        string reasonCode,
        GuardrailAction action = GuardrailAction.Allow,
        AuditSeverity severity = AuditSeverity.Info,
        BillingOwner billingOwner = BillingOwner.Org,
        Action<AuditEvent>? configure = null,
        CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            ActorLabel = actorLabel,
            ReasonCode = reasonCode,
            Action = action,
            Severity = severity,
            BillingOwner = billingOwner
        };

        configure?.Invoke(evt);
        await AppendAsync(evt, ct);
    }

    public static string Canonicalize(AuditEvent e)
    {
        // 显式按固定顺序投影，避免匿名对象属性顺序变化导致哈希漂移。
        var payload = new
        {
            e.EventId,
            e.OccurredAt,
            e.Sequence,
            e.EventType,
            e.Severity,
            e.ActorType,
            e.ActorId,
            e.ActorLabel,
            e.AppId,
            e.TeamId,
            e.RequestId,
            e.ModelAlias,
            e.ProviderId,
            e.UpstreamModel,
            e.ModelGroupId,
            e.ModelMemberId,
            e.Action,
            e.PolicyId,
            e.PolicyVersion,
            e.RuleId,
            e.ReasonCode,
            e.Detail,
            e.InputTokens,
            e.InputTokensOriginal,
            e.OutputTokens,
            e.CostUsd,
            e.IpAddress,
            e.UserAgent,
            e.ContentHash,
            e.ContentMissing,
            e.BillingOwner
        };

        return JsonSerializer.Serialize(payload, CanonicalJson);
    }

    public static string ComputeHash(string prevHash, string canonicalJson)
    {
        var payload = $"{prevHash}\n{canonicalJson}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>校验区间内的链完整性，返回首个断点 sequence（null 表示通过）。</summary>
    public static long? Verify(IEnumerable<AuditEvent> ordered, string startingPrevHash = "")
    {
        var expectedPrev = startingPrevHash;
        foreach (var evt in ordered.OrderBy(e => e.Sequence))
        {
            if (!string.IsNullOrEmpty(expectedPrev) &&
                !string.Equals(evt.PrevHash, expectedPrev, StringComparison.Ordinal))
            {
                return evt.Sequence;
            }

            var recomputed = ComputeHash(evt.PrevHash, Canonicalize(evt));
            if (!string.Equals(recomputed, evt.Hash, StringComparison.Ordinal))
            {
                return evt.Sequence;
            }

            expectedPrev = evt.Hash;
        }

        return null;
    }
}
