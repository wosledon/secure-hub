using SecureHub.Core.Domain;

namespace SecureHub.Core.Security;

public enum Permission
{
    ProviderRead,
    ProviderWrite,
    CatalogRead,
    CatalogWrite,
    RoutingRead,
    RoutingWrite,
    SecurityRead,
    SecurityWrite,
    KeyRead,
    KeyWrite,
    BudgetWrite,
    UsageRead,
    AuditRead,
    AuditExport,
    AssistantUse,
    SystemWrite,
    UserWrite,
    ContentRead
}

/// <summary>角色权限矩阵（PRD §7.1.4）。EndUser 的资源范围由服务端按属主收窄，不在此表授权。</summary>
public static class Rbac
{
    private static readonly Dictionary<UserRole, HashSet<Permission>> Matrix = new()
    {
        [UserRole.PlatformAdmin] =
        [
            Permission.ProviderRead, Permission.ProviderWrite,
            Permission.CatalogRead, Permission.CatalogWrite,
            Permission.RoutingRead, Permission.RoutingWrite,
            Permission.SecurityRead, Permission.SecurityWrite,
            Permission.KeyRead, Permission.KeyWrite,
            Permission.BudgetWrite, Permission.UsageRead,
            Permission.AuditRead, Permission.AuditExport,
            Permission.AssistantUse, Permission.SystemWrite, Permission.UserWrite, Permission.ContentRead
        ],
        [UserRole.SecurityAdmin] =
        [
            Permission.ProviderRead, Permission.CatalogRead,
            Permission.RoutingRead, Permission.RoutingWrite,
            Permission.SecurityRead, Permission.SecurityWrite,
            Permission.KeyRead, Permission.UsageRead,
            Permission.AuditRead, Permission.AuditExport,
            Permission.AssistantUse, Permission.ContentRead
        ],
        [UserRole.TeamOwner] =
        [
            Permission.ProviderRead, Permission.CatalogRead,
            Permission.RoutingRead, Permission.RoutingWrite,
            Permission.SecurityRead, Permission.KeyRead, Permission.KeyWrite,
            Permission.BudgetWrite, Permission.UsageRead,
            Permission.AuditRead, Permission.AssistantUse
        ],
        [UserRole.Developer] =
        [
            Permission.ProviderRead, Permission.CatalogRead,
            Permission.RoutingRead, Permission.KeyWrite,
            Permission.UsageRead
        ],
        [UserRole.Auditor] =
        [
            Permission.ProviderRead, Permission.CatalogRead,
            Permission.RoutingRead, Permission.SecurityRead,
            Permission.KeyRead, Permission.UsageRead,
            Permission.AuditRead, Permission.AuditExport,
            Permission.AssistantUse
        ],
        [UserRole.EndUser] =
        [
            Permission.CatalogRead, Permission.UsageRead,
            Permission.AssistantUse
        ]
    };

    public static bool Allows(UserRole role, Permission permission) =>
        Matrix.TryGetValue(role, out var set) && set.Contains(permission);

    public static bool AllowsAny(IReadOnlyList<UserRole> roles, Permission permission) =>
        roles.Any(role => Allows(role, permission));

    /// <summary>个人用户只能看到自己的数据；管理角色按团队/全量收窄由查询层负责。</summary>
    public static bool IsPersonalScope(IReadOnlyList<UserRole> roles) =>
        roles.Count > 0 && roles.All(r => r == UserRole.EndUser);
}
