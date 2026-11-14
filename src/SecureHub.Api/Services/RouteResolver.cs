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
        var now = DateTimeOffset.UtcNow;

        var matches = new List<(Provider Provider, ProviderOffering Offering)>();

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

            matches.Add((provider, offering));
        }

        if (matches.Count == 0)
        {
            return RouteDecision.Fail(modelId, ReasonCodes.ModelNotFound);
        }

        var candidates = matches
            .Select(m => new RouteCandidate(
                m.Offering.Id,
                modelId.Scope,
                m.Provider.Id,
                m.Provider.Alias,
                m.Offering.UpstreamModel,
                Priority: 100,
                Weight: 100,
                MemberState.Healthy))
            .ToList();

        var displayList = candidates.Select(c => $"{ModelId.ScopePrefix(modelId.Scope)}/{c.ProviderAlias}/{c.UpstreamModel}").ToList();

        if (candidates.Count == 1)
        {
            return new RouteDecision(true, null, modelId, candidates[0], displayList, ToBilling(modelId.Scope));
        }

        if (!options.Value.LoadBalancing.Enabled)
        {
            return RouteDecision.Fail(modelId, ReasonCodes.ModelAmbiguous, displayList);
        }

        var selected = LoadBalancer.Select(candidates, options.Value.LoadBalancing.HealthPenalty);
        return selected is null
            ? RouteDecision.Fail(modelId, ReasonCodes.AllCandidatesFailed, displayList)
            : new RouteDecision(true, null, modelId, selected, displayList, ToBilling(modelId.Scope));
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
