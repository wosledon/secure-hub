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
    Guid? AppId,
    Guid? TeamId,
    BillingOwner Billing,
    KeyConstraints Constraints);

/// <summary>虚拟密钥校验（security.md §4.1）：语法 → 存在性 → 哈希 → 状态，失败原因不外泄。</summary>
public sealed class ApiKeyAuthenticator(SecureHubDbContext db, IClock clock)
{
    public async Task<(AuthContext? Context, string? ErrorCode)> AuthenticateAsync(
        string? rawKey,
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

        if (key.OwnerType == KeyOwnerType.App)
        {
            var app = await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == key.OwnerId, ct);
            if (app is null) return (null, ReasonCodes.InvalidApiKey);

            return (new AuthContext(
                key.Id, key.OwnerType, key.OwnerId, app.Name, app.Id, app.TeamId, BillingOwner.Org,
                KeyConstraints.Parse(key.ConstraintsJson)), null);
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == key.OwnerId, ct);
        if (user is null || !user.IsActive) return (null, ReasonCodes.InvalidApiKey);

        return (new AuthContext(
            key.Id, key.OwnerType, key.OwnerId, user.UserName, null, user.TeamId, BillingOwner.Org,
            KeyConstraints.Parse(key.ConstraintsJson)), null);
    }
}
