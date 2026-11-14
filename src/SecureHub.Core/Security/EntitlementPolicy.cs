using SecureHub.Core.Domain;

namespace SecureHub.Core.Security;

/// <summary>
/// Entitlement 判定：某供应商一旦配置了分配，就按分配严格判断；
/// 从未配置分配的供应商视为“未启用分配”，允许访问（便于冷启动）。
/// </summary>
public static class EntitlementPolicy
{
    public static bool AllowsProvider(
        IReadOnlyList<Entitlement> entitlements,
        Guid providerId,
        Guid? teamId,
        Guid ownerId,
        DateTimeOffset now)
    {
        var forProvider = entitlements.Where(e => e.ProviderId == providerId).ToList();
        if (forProvider.Count == 0) return true;

        return forProvider.Any(e => IsActiveFor(e, teamId, ownerId, now));
    }

    public static bool AllowsModel(
        IReadOnlyList<Entitlement> entitlements,
        Guid providerId,
        string upstreamModel,
        Guid? teamId,
        Guid ownerId,
        DateTimeOffset now)
    {
        var forProvider = entitlements.Where(e => e.ProviderId == providerId).ToList();
        if (forProvider.Count == 0) return true;

        return forProvider.Any(e =>
            IsActiveFor(e, teamId, ownerId, now) && MatchesModels(e.ModelsJson, upstreamModel));
    }

    private static bool IsActiveFor(Entitlement entitlement, Guid? teamId, Guid ownerId, DateTimeOffset now) =>
        (entitlement.ExpiresAt is null || entitlement.ExpiresAt > now) &&
        (entitlement.SubjectType == SubjectType.All ||
         (entitlement.SubjectType == SubjectType.Team && entitlement.SubjectId == teamId) ||
         (entitlement.SubjectType == SubjectType.User && entitlement.SubjectId == ownerId));

    /// <summary>ModelsJson 为空表示该供应商全部模型；否则按 upstream / canonical / group 名称匹配。</summary>
    private static bool MatchesModels(string? modelsJson, string upstreamModel)
    {
        if (string.IsNullOrWhiteSpace(modelsJson)) return true;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(modelsJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return true;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String &&
                    string.Equals(item.GetString(), upstreamModel, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            // 配置损坏时按“未限制”处理，避免误伤流量；写入口会校验格式。
            return true;
        }
    }
}
