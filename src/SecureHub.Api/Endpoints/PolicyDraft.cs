using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Core.Security.Policies;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>
/// 策略草稿的校验、构建与发布（PRD §7.3）。HTTP 端点与助手工具共用一份实现，
/// 避免两处校验规则漂移——助手走的路径必须和管理端走的路径完全一致。
/// </summary>
public static class PolicyDraft
{
    private static readonly Regex KeyPattern =
        new("^[a-z][a-z0-9-]{1,63}$", RegexOptions.Compiled);

    /// <summary>校验草稿，返回机器可读错误码；null 表示通过。与 <c>error.*</c> 本地化键对应。</summary>
    public static string? Validate(PublishPolicyRequest request, CustomRuleStore customRules)
    {
        if (!KeyPattern.IsMatch(request.PolicyKey ?? string.Empty))
        {
            return "validation.policy_key";
        }

        if (request.Priority is < 0 or > 1000)
        {
            return "validation.policy_priority";
        }

        var action = (request.Then?.Action ?? "block").Trim().ToLowerInvariant();
        if (action is not ("block" or "redact" or "alert"))
        {
            return "validation.policy_action";
        }

        var any = request.When?.Any ?? [];
        var all = request.When?.All ?? [];
        foreach (var requirement in any.Concat(all))
        {
            // 内置 id 或已存在的自定义规则 id（custom.{key}）均可引用。
            var known = DetectorCatalog.Contains(requirement.Detector) ||
                        customRules.Contains(requirement.Detector);
            if (!known)
            {
                return "validation.detector_unknown";
            }

            if (requirement.MinMatches is < 0 or > 100)
            {
                return "validation.invalid";
            }
        }

        var hasScope =
            (request.Scope?.Teams?.Length ?? 0) > 0 ||
            (request.Scope?.Apps?.Length ?? 0) > 0 ||
            (request.Scope?.Models?.Length ?? 0) > 0;

        if (!hasScope && any.Length == 0 && all.Length == 0)
        {
            return "validation.policy_condition_empty";
        }

        return null;
    }

    public static PolicyScope BuildScope(PublishPolicyRequest request) => new()
    {
        Teams = request.Scope?.Teams ?? [],
        Apps = request.Scope?.Apps ?? [],
        Models = request.Scope?.Models ?? []
    };

    public static PolicyCondition BuildCondition(PublishPolicyRequest request) => new()
    {
        Any = (request.When?.Any ?? []).Select(d => new DetectorRequirement
        {
            Detector = d.Detector,
            MinMatches = Math.Max(1, d.MinMatches)
        }).ToArray(),
        All = (request.When?.All ?? []).Select(d => new DetectorRequirement
        {
            Detector = d.Detector,
            MinMatches = Math.Max(1, d.MinMatches)
        }).ToArray()
    };

    public static PolicyThen BuildThen(PublishPolicyRequest request)
    {
        var action = (request.Then?.Action ?? "block").Trim().ToLowerInvariant();
        var firstDetector = request.When?.Any?.FirstOrDefault()?.Detector
            ?? request.When?.All?.FirstOrDefault()?.Detector;

        return new PolicyThen
        {
            Action = action,
            Reason = string.IsNullOrWhiteSpace(request.Then?.Reason) ? "policy_matched" : request.Then!.Reason,
            RuleId = string.IsNullOrWhiteSpace(request.Then?.RuleId) ? firstDetector : request.Then!.RuleId
        };
    }

    /// <summary>发布结果；审计日志由调用方写（端点写用户名，助手写 <c>assistant</c>）。</summary>
    public sealed record PublishResult(
        string PolicyKey,
        int Version,
        bool Created,
        string Action,
        bool SnapshotReloaded,
        string? StoreError);

    /// <summary>
    /// 落库并刷新快照：策略查找/创建、版本递增、版本写入、原子刷新。
    /// 调用方需先通过 <see cref="Validate"/>。
    /// </summary>
    public static async Task<PublishResult> PublishAsync(
        SecureHubDbContext db, PolicyStore store, PublishPolicyRequest request,
        Guid createdBy, IClock clock, CancellationToken ct)
    {
        var policy = await db.SecurityPolicies.FirstOrDefaultAsync(p => p.PolicyKey == request.PolicyKey, ct);
        var created = policy is null;
        if (policy is null)
        {
            policy = new SecurityPolicy
            {
                PolicyKey = request.PolicyKey,
                Enabled = true,
                CreatedAt = clock.UtcNow
            };
            db.SecurityPolicies.Add(policy);
        }

        var nextVersion = (await db.SecurityPolicyVersions
            .Where(v => v.PolicyId == policy.Id)
            .Select(v => (int?)v.Version)
            .MaxAsync(ct) ?? 0) + 1;

        var then = BuildThen(request);
        db.SecurityPolicyVersions.Add(new SecurityPolicyVersion
        {
            PolicyId = policy.Id,
            Version = nextVersion,
            Priority = request.Priority,
            ScopeJson = JsonSerializer.Serialize(BuildScope(request)),
            WhenJson = JsonSerializer.Serialize(BuildCondition(request)),
            ThenJson = JsonSerializer.Serialize(then),
            OnError = string.Equals(request.OnError, "allow", StringComparison.OrdinalIgnoreCase) ? "allow" : "block",
            RolloutJson = JsonSerializer.Serialize(new PolicyRollout { Percent = request.RolloutPercent ?? 100 }),
            ChangeReason = request.ChangeReason?.Trim() ?? string.Empty,
            CreatedBy = createdBy,
            CreatedAt = clock.UtcNow
        });

        policy.ActiveVersion = nextVersion;
        await db.SaveChangesAsync(ct);

        var refreshed = await store.RefreshAsync(ct);

        return new PublishResult(
            policy.PolicyKey, nextVersion, created, then.Action, refreshed, store.LastError);
    }
}
