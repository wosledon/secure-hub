using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Guardrails;

public sealed record DetectorDescriptor(
    string Id,
    string Category,
    GuardrailSeverity Severity,
    string Sample);

/// <summary>
/// 检测器目录：管理端展示、策略校验与试跑共用同一组 ID。
/// Sample 必须能被对应检测器命中，供控制台一键试跑。
/// </summary>
public static class DetectorCatalog
{
    public static IReadOnlyList<DetectorDescriptor> All { get; } =
    [
        new("pii.cn_idcard", "pii", GuardrailSeverity.Critical, "11010519491231002X"),
        new("pii.cn_mobile", "pii", GuardrailSeverity.Warning, "13800138000"),
        new("pii.bank_card", "pii", GuardrailSeverity.Critical, "4111111111111111"),
        new("pii.email", "pii", GuardrailSeverity.Info, "alice@example.com"),
        new("secret.private_key", "secret", GuardrailSeverity.Critical, "-----BEGIN RSA PRIVATE KEY-----"),
        new("secret.jwt", "secret", GuardrailSeverity.Warning,
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U"),
        new("secret.generic", "secret", GuardrailSeverity.Critical, "sk-live-9fK2mQ7vT1xR4pL8sN3wZ6bC")
    ];

    public static bool Contains(string id) =>
        All.Any(detector => string.Equals(detector.Id, id, StringComparison.Ordinal));

    public static DetectorDescriptor? Find(string id) =>
        All.FirstOrDefault(detector => string.Equals(detector.Id, id, StringComparison.Ordinal));
}
