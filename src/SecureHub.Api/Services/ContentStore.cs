using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>内容表读写：加密、截断、留存（security.md §8）。</summary>
public sealed class ContentStore(
    SecureHubDbContext db,
    ContentCipher cipher,
    IClock clock,
    IOptions<SecureHubOptions> options)
{
    public async Task SaveAsync(
        string requestId,
        ContentDirection direction,
        ContentVariant variant,
        Guid? appId,
        string text,
        bool force = false,
        CancellationToken ct = default)
    {
        var settings = options.Value.Content;
        // force：员工流量（含 BYOK）必须落盘，不受全局开关影响。
        if (!force && !settings.PersistContent) return;

        var truncated = false;
        if (text.Length > settings.MaxContentBytes)
        {
            text = text[..settings.MaxContentBytes];
            truncated = true;
        }

        var aad = ContentCipher.BuildAad(requestId, direction.ToString(), variant.ToString());
        var (cipherBytes, nonce, tag) = cipher.Seal(text, aad);

        var days = variant switch
        {
            ContentVariant.Raw => settings.RawRetentionDays,
            ContentVariant.Redacted => settings.RedactedRetentionDays,
            _ => settings.CompressedRetentionDays
        };

        var now = clock.UtcNow;
        db.ContentBlobs.Add(new ContentBlob
        {
            RequestId = requestId,
            Direction = direction,
            Variant = variant,
            AppId = appId,
            CipherText = cipherBytes,
            Nonce = nonce,
            Tag = tag,
            DekId = cipher.DekId,
            ContentHash = ContentCipher.HashContent(text),
            SizeBytes = text.Length,
            Truncated = truncated,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days)
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> ReadAsync(
        string requestId,
        ContentDirection direction,
        ContentVariant variant,
        CancellationToken ct = default)
    {
        var blob = await db.ContentBlobs
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.RequestId == requestId && b.Direction == direction && b.Variant == variant, ct);

        if (blob is null) return null;

        var aad = ContentCipher.BuildAad(requestId, direction.ToString(), variant.ToString());
        return cipher.Open(blob.CipherText, blob.Nonce, blob.Tag, aad);
    }
}
