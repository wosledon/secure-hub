using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Auth;

public sealed record AuthContext(
    Guid VirtualKeyId,
    KeyOwnerType OwnerType,
    Guid OwnerId,
    string Label,
    string? AppName,
    Guid? TeamId,
    BillingOwner Billing,
    KeyConstraints Constraints)
{
    public string AuditActorType => OwnerType == KeyOwnerType.User ? "user" : "virtualKey";

    public string AuditActorId => OwnerType == KeyOwnerType.User ? OwnerId.ToString() : VirtualKeyId.ToString();
}

/// <summary>虚拟密钥校验（security.md §4.1）：语法 → 存在性 → 哈希 → 状态，失败原因不外泄。</summary>
public sealed class ApiKeyAuthenticator(SecureHubDbContext db, IClock clock)
{
    public async Task<(AuthContext? Context, string? ErrorCode)> AuthenticateAsync(
        string? rawKey,
        string? requestAppName = null,
        CancellationToken ct = default)
    {
        if (!VirtualKeyFormat.TryParse(rawKey, out var parsed))
        {
            return (null, ReasonCodes.InvalidApiKey);
        }

        var key = await db.VirtualKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyId == parsed.KeyId, ct);

        if (key is null) return (null, ReasonCodes.InvalidApiKey);

        var hash = VirtualKeyFormat.HashSecret(parsed.Secret);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, key.SecretHash))
        {
            return (null, ReasonCodes.InvalidApiKey);
        }

        if (!key.IsActive(clock.UtcNow)) return (null, ReasonCodes.InvalidApiKey);

        if (key.OwnerType == KeyOwnerType.Organization)
        {
            var appName = ResolveAppName(key.AppName, requestAppName);
            var label = string.IsNullOrWhiteSpace(key.DisplayName) ? "organization" : key.DisplayName;
            return (new AuthContext(
                key.Id, key.OwnerType, key.OwnerId, label, appName, key.TeamId, BillingOwner.Org,
                KeyConstraints.Parse(key.ConstraintsJson)), null);
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == key.OwnerId, ct);
        if (user is null || !user.IsActive) return (null, ReasonCodes.InvalidApiKey);

        return (new AuthContext(
            key.Id, key.OwnerType, key.OwnerId, user.UserName, ResolveAppName(key.AppName, requestAppName),
            user.TeamId, BillingOwner.Org, KeyConstraints.Parse(key.ConstraintsJson)), null);
    }

    private static string? ResolveAppName(string? keyAppName, string? requestAppName)
    {
        var value = string.IsNullOrWhiteSpace(requestAppName) ? keyAppName : requestAppName;
        if (string.IsNullOrWhiteSpace(value)) return null;

        var sanitized = new string(value.Trim().Where(ch => !char.IsControl(ch)).ToArray());
        return sanitized.Length <= 100 ? sanitized : sanitized[..100];
    }

    public static string? RequestAppName(HttpRequest request)
    {
        var value = request.Headers["X-SecureHub-App"].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
