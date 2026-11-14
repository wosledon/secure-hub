using Microsoft.AspNetCore.DataProtection;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>上游凭据的加解密（ASP.NET DataProtection）。</summary>
public sealed class ProviderCredentialStore(IDataProtectionProvider provider)
{
    private IDataProtector Protector => provider.CreateProtector("securehub.provider-credential");

    public byte[] Protect(string apiKey) =>
        Protector.Protect(System.Text.Encoding.UTF8.GetBytes(apiKey));

    public string Unprotect(byte[] cipherText) =>
        System.Text.Encoding.UTF8.GetString(Protector.Unprotect(cipherText));

    public async Task<string?> GetApiKeyAsync(
        SecureHubDbContext db,
        Guid providerId,
        Guid? ownerUserId,
        CancellationToken ct = default)
    {
        var credential = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(
                db.ProviderCredentials
                    .Where(c => c.ProviderId == providerId && c.RetiredAt == null)
                    .OrderByDescending(c => c.CreatedAt),
                ct);

        if (credential is null) return null;
        if (ownerUserId is not null && credential.OwnerUserId != ownerUserId) return null;

        return Unprotect(credential.CipherText);
    }
}
