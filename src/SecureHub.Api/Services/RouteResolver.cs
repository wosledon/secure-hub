using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Core.Domain;
using SecureHub.Core.Models;
using SecureHub.Core.Options;
using SecureHub.Data;

namespace SecureHub.Api.Services;

public sealed record RouteDecision(
    bool Success,
    string? ErrorCode,
    ModelId Model,
    RouteCandidate? Candidate,
    IReadOnlyList<string> Candidates,
    BillingOwner BillingOwner)
{
    public static RouteDecision Fail(ModelId model, string errorCode, IReadOnlyList<string>? candidates = null) =>
        new(false, errorCode, model, null, candidates ?? [], BillingOwner.Org);
}

/// <summary>
/// 解析模型 ID → 候选 offering → 唯一命中或负载均衡（PRD §10.3 / §6.6）。
/// </summary>
public sealed class RouteResolver(
    SecureHubDbContext db,
    IOptions<SecureHubOptions> options)
{
    public async Task<RouteDecision> ResolveAsync(
        string rawModel,
        KeyOwnerType ownerType,
        Guid ownerId,
        Guid? teamId,
        CancellationToken ct = default)
    {
        var providers = await db.Providers.AsNoTracking()
            .Where(p => p.Enabled)
            .ToListAsync(ct);

        var aliases = providers.Select(p => p.Alias).ToHashSet(StringComparer.Ordinal);

        if (!ModelId.TryParse(rawModel, aliases, out var modelId, out var parseError))
        {
            return RouteDecision.Fail(default, parseError ?? ReasonCodes.ModelNotFound);
        }

        var offerings = await db.ProviderOfferings.AsNoTracking().ToListAsync(ct);
        var entitlements = await db.Entitlements.AsNoTracking().ToListAsync(ct);
        var groups = await db.ModelGroups.AsNoTracking().Where(g => g.Enabled).ToListAsync(ct);
        var members = await db.ModelGroupMembers.AsNoTracking().ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var candidates = new List<RouteCandidate>();

        // 1) 显式建模组优先（PRD §6.6.2）
        if (modelId.ProviderAlias is null)
        {
            var explicitGroup = groups.FirstOrDefault(g =>
                !g.IsAuto && string.Equals(g.Name, modelId.Model, StringComparison.Ordinal));

            if (explicitGroup is not null)
            {
                foreach (var member in members.Where(m => m.GroupId == explicitGroup.Id && m.Enabled))
                {
                    var provider = providers.FirstOrDefault(p => p.Id == member.ProviderId);
                    if (provider is null) continue;

                    if (!ScopeVisible(member.Scope, provider, explicitGroup.AllowCrossScope, ownerType, ownerId)) continue;
                    if (member.Scope == ModelScope.Org &&
                        !IsEntitled(entitlements, provider.Id, teamId, ownerId, now)) continue;

                    candidates.Add(new RouteCandidate(
                        member.Id,
                        member.Scope,
                        provider.Id,
                        provider.Alias,
                        member.UpstreamModel,
                        member.Priority,
                        member.Weight,
                        member.State));
                }
            }
        }

        // 2) 回退到 offering 匹配（同名自动成组由负载均衡器处理）
        if (candidates.Count == 0)
        {
            foreach (var offering in offerings.Where(o => o.Enabled))
            {
                var provider = providers.FirstOrDefault(p => p.Id == offering.ProviderId);
                if (provider is null) continue;

                if (!ModelMatches(offering, modelId.Model)) continue;

                if (modelId.Scope == ModelScope.User)
                {
                    // 个人命名空间：必须是自己 BYOK 的供应商
                    if (provider.Scope != ProviderScope.User) continue;
                    if (provider.OwnerUserId != ownerId) continue;
                    if (ownerType != KeyOwnerType.User) continue;
                }
                else
                {
                    if (provider.Scope != ProviderScope.Org) continue;
                    if (modelId.ProviderAlias is not null &&
                        !string.Equals(provider.Alias, modelId.ProviderAlias, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!IsEntitled(entitlements, provider.Id, teamId, ownerId, now)) continue;
                }

                candidates.Add(new RouteCandidate(
                    offering.Id,
                    modelId.Scope,
                    provider.Id,
                    provider.Alias,
                    offering.UpstreamModel,
                    Priority: 100,
                    Weight: 100,
                    MemberState.Healthy));
            }
        }

        if (candidates.Count == 0)
        {
            return RouteDecision.Fail(modelId, ReasonCodes.ModelNotFound);
        }

        var displayList = candidates
            .Select(c => $"{ModelId.ScopePrefix(c.Scope)}/{c.ProviderAlias}/{c.UpstreamModel}")
            .ToList();

        if (candidates.Count == 1)
        {
            return new RouteDecision(true, null, modelId, candidates[0], displayList, ToBilling(candidates[0].Scope));
        }

        if (!options.Value.LoadBalancing.Enabled)
        {
            return RouteDecision.Fail(modelId, ReasonCodes.ModelAmbiguous, displayList);
        }

        var selected = LoadBalancer.Select(candidates, options.Value.LoadBalancing.HealthPenalty);
        return selected is null
            ? RouteDecision.Fail(modelId, ReasonCodes.AllCandidatesFailed, displayList)
            : new RouteDecision(true, null, modelId, selected, displayList, ToBilling(selected.Scope));
    }

    /// <summary>
    /// 组内可见性：企业成员对所有人可见；个人成员仅在本人请求且组允许跨 scope 时参与。
    /// 显式 <c>me/...</c> 路径不走这里，始终可用自己的凭据。
    /// </summary>
    private static bool ScopeVisible(
        ModelScope memberScope,
        Provider provider,
        bool allowCrossScope,
        KeyOwnerType ownerType,
        Guid ownerId)
    {
        if (memberScope == ModelScope.Org)
        {
            return provider.Scope == ProviderScope.Org;
        }

        if (ownerType != KeyOwnerType.User) return false;
        if (!allowCrossScope) return false;
        return provider.Scope == ProviderScope.User && provider.OwnerUserId == ownerId;
    }

    private static BillingOwner ToBilling(ModelScope scope) =>
        scope == ModelScope.User ? BillingOwner.User : BillingOwner.Org;

    private static bool ModelMatches(ProviderOffering offering, string model) =>
        string.Equals(offering.UpstreamModel, model, StringComparison.Ordinal) ||
        string.Equals(offering.CanonicalModelId, model, StringComparison.Ordinal) ||
        string.Equals(offering.GroupKey, model, StringComparison.Ordinal);

    /// <summary>
    /// 分配规则：某供应商一旦配置了 Entitlement，就按分配严格判断；
    /// 从未配置分配的供应商视为"未启用分配"，允许访问（便于冷启动）。
    /// </summary>
    private static bool IsEntitled(
        IReadOnlyList<Entitlement> entitlements,
        Guid providerId,
        Guid? teamId,
        Guid ownerId,
        DateTimeOffset now)
    {
        var forProvider = entitlements.Where(e => e.ProviderId == providerId).ToList();
        if (forProvider.Count == 0) return true;

        return forProvider.Any(e =>
            (e.ExpiresAt is null || e.ExpiresAt > now) &&
            (e.SubjectType == SubjectType.All ||
             (e.SubjectType == SubjectType.Team && e.SubjectId == teamId) ||
             (e.SubjectType == SubjectType.User && e.SubjectId == ownerId)));
    }
}
