using SecureHub.Core.Domain;

namespace SecureHub.Core.Models;

/// <summary>路由候选：一个 (scope, provider, model) 的具体 offering。</summary>
public sealed record RouteCandidate(
    Guid MemberId,
    ModelScope Scope,
    Guid ProviderId,
    string ProviderAlias,
    string UpstreamModel,
    int Priority,
    int Weight,
    MemberState State)
{
    public bool Selectable => State != MemberState.Disabled && Weight > 0;
}

/// <summary>
/// 负载均衡选择（PRD §6.6.3）：
/// 按 priority 降序取最高档，档内按 weight 加权随机；
/// degraded 成员权重乘以 healthPenalty。
/// </summary>
public static class LoadBalancer
{
    public static RouteCandidate? Select(
        IReadOnlyList<RouteCandidate> candidates,
        double healthPenalty = 0.5,
        Random? random = null)
    {
        if (candidates.Count == 0) return null;

        var selectable = candidates.Where(c => c.Selectable).ToArray();
        if (selectable.Length == 0) return null;

        var topPriority = selectable.Max(c => c.Priority);
        var tier = selectable.Where(c => c.Priority == topPriority).ToArray();

        if (tier.Length == 1) return tier[0];

        var effective = new double[tier.Length];
        var total = 0d;
        for (var i = 0; i < tier.Length; i++)
        {
            var weight = (double)tier[i].Weight;
            if (tier[i].State == MemberState.Degraded)
            {
                weight *= Math.Clamp(healthPenalty, 0.01, 1.0);
            }

            effective[i] = weight;
            total += weight;
        }

        if (total <= 0) return tier[0];

        var rng = random ?? Random.Shared;
        var roll = rng.NextDouble() * total;
        var cumulative = 0d;
        for (var i = 0; i < tier.Length; i++)
        {
            cumulative += effective[i];
            if (roll <= cumulative) return tier[i];
        }

        return tier[^1];
    }

    /// <summary>降级后继续尝试：排除已失败的成员。</summary>
    public static RouteCandidate? SelectNext(
        IReadOnlyList<RouteCandidate> candidates,
        IReadOnlySet<Guid> failedMemberIds,
        double healthPenalty = 0.5,
        Random? random = null) =>
        Select(candidates.Where(c => !failedMemberIds.Contains(c.MemberId)).ToArray(), healthPenalty, random);
}
