using System.Text;
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
    IOptions<SecureHubOptions> options,
    SystemSettingStore runtime)
{
    public async Task SaveAsync(
        string requestId,
        ContentDirection direction,
        ContentVariant variant,
        string? appName,
        string text,
        bool force = false,
        CancellationToken ct = default)
    {
        var settings = options.Value.Content;
        // force：员工流量（含 BYOK）必须落盘，不受全局开关影响。
        var persist = runtime.GetBool("content.persist", settings.PersistContent);
        if (!force && !persist) return;

        var maxBytes = runtime.GetInt("content.max_bytes", settings.MaxContentBytes, 1024, 16 * 1024 * 1024);
        var (truncatedText, truncated) = TruncateToBytes(text, maxBytes);

        var aad = ContentCipher.BuildAad(requestId, direction.ToString(), variant.ToString());
        var (cipherBytes, nonce, tag) = cipher.Seal(truncatedText, aad);

        var days = variant switch
        {
            ContentVariant.Raw => runtime.GetInt("content.raw_days", settings.RawRetentionDays, 1, 3650),
            ContentVariant.Redacted => runtime.GetInt("content.redacted_days", settings.RedactedRetentionDays, 1, 3650),
            _ => runtime.GetInt("content.compressed_days", settings.CompressedRetentionDays, 1, 3650)
        };

        var now = clock.UtcNow;
        db.ContentBlobs.Add(new ContentBlob
        {
            RequestId = requestId,
            Direction = direction,
            Variant = variant,
            AppName = appName,
            CipherText = cipherBytes,
            Nonce = nonce,
            Tag = tag,
            DekId = cipher.DekId,
            ContentHash = ContentCipher.HashContent(truncatedText),
            SizeBytes = Encoding.UTF8.GetByteCount(truncatedText),
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

        // 过期即不可读：留存期是合规承诺，不能因为"行还在"就继续返回正文。
        if (blob.ExpiresAt <= clock.UtcNow) return null;

        var aad = ContentCipher.BuildAad(requestId, direction.ToString(), variant.ToString());
        return cipher.Open(blob.CipherText, blob.Nonce, blob.Tag, aad);
    }

    /// <summary>
    /// 按 UTF-8 字节截断。配置项叫 MaxContentBytes，因此不能用字符数判断——
    /// 中文一个字 3 字节，按字符截会让实际体积超限数倍。
    /// </summary>
    private static (string Text, bool Truncated) TruncateToBytes(string text, int maxBytes)
    {
        if (maxBytes <= 0) return (text, false);

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes) return (text, false);

        var length = maxBytes;

        // 退到 UTF-8 字符边界，避免截出半个多字节序列。
        while (length > 0 && (bytes[length] & 0b1100_0000) == 0b1000_0000)
        {
            length--;
        }

        return (Encoding.UTF8.GetString(bytes, 0, length), true);
    }
}
