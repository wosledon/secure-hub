using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>
/// tokenize 脱敏（PRD §7.4）：把敏感值换成 <c>⟦sh:{32位hex}⟧</c> 发给上游，
/// 密文存 <see cref="RedactionToken"/>，响应回来时按令牌还原。
/// <para>
/// 与 mask/placeholder 的区别：前两者不可逆，模型只能看到残缺值；
/// tokenize 让模型看到稳定占位符，同时平台保有原文，响应里的占位符可以还原成真实值。
/// </para>
/// </summary>
public sealed class RedactionTokenStore(
    SecureHubDbContext db,
    ContentCipher cipher,
    IClock clock,
    IOptions<SecureHubOptions> options,
    SystemSettingStore runtime)
{
    /// <summary>令牌格式：⟦sh:&lt;32 hex&gt;⟧。用非 ASCII 括号降低与业务文本碰撞的概率。</summary>
    private static readonly Regex TokenPattern = new(
        @"⟦sh:([0-9a-f]{32})⟧",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BuildToken(string tokenId) => $"⟦sh:{tokenId}⟧";

    public static bool ContainsToken(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("⟦sh:", StringComparison.Ordinal);

    /// <summary>把命中的敏感值替换为令牌，并持久化密文。返回改写后的文本与替换次数。</summary>
    public async Task<(string Text, int Replaced)> TokenizeAsync(
        string requestId,
        string text,
        IEnumerable<Detection> detections,
        CancellationToken ct = default)
    {
        var result = text;
        var replaced = 0;
        var index = 0;
        var now = clock.UtcNow;
        var expires = now.AddHours(runtime.GetInt(
            "guardrails.token_retention_hours",
            options.Value.Guardrails.TokenRetentionHours, 1, 720));

        foreach (var detection in detections)
        {
            foreach (var value in detection.MatchedValues)
            {
                if (value.Length == 0 || value.StartsWith('<')) continue;
                if (!result.Contains(value, StringComparison.Ordinal)) continue;

                var tokenId = Guid.NewGuid().ToString("N");
                var aad = BuildAad(requestId, tokenId);
                var (cipherText, nonce, tag) = cipher.Seal(value, aad);

                db.RedactionTokens.Add(new RedactionToken
                {
                    TokenId = tokenId,
                    RequestId = requestId,
                    DetectorId = detection.DetectorId,
                    CipherText = cipherText,
                    Nonce = nonce,
                    Tag = tag,
                    DekId = cipher.DekId,
                    PlainLength = value.Length,
                    CreatedAt = now,
                    ExpiresAt = expires
                });

                result = result.Replace(value, BuildToken(tokenId), StringComparison.Ordinal);
                replaced++;
                index++;
            }
        }

        if (index > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return (result, replaced);
    }

    /// <summary>把响应里的令牌还原成原文。过期或不属于本请求的令牌保持原样。</summary>
    public async Task<string> RestoreAsync(string requestId, string text, CancellationToken ct = default)
    {
        if (!ContainsToken(text)) return text;

        var matches = TokenPattern.Matches(text);
        if (matches.Count == 0) return text;

        var ids = matches
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var now = clock.UtcNow;
        var rows = await db.RedactionTokens
            .AsNoTracking()
            .Where(t => ids.Contains(t.TokenId) && t.RequestId == requestId && t.ExpiresAt > now)
            .ToListAsync(ct);

        if (rows.Count == 0) return text;

        var map = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                map[row.TokenId] = cipher.Open(
                    row.CipherText, row.Nonce, row.Tag, BuildAad(row.RequestId, row.TokenId));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // AAD 不匹配或密钥已轮换：保留占位符，不猜测原文。
            }
        }

        if (map.Count == 0) return text;

        return TokenPattern.Replace(text, m =>
            map.TryGetValue(m.Groups[1].Value, out var plain) ? plain : m.Value);
    }

    /// <summary>清理过期令牌（不留长期可逆映射）。</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return await db.RedactionTokens
            .Where(t => t.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
    }

    private static string BuildAad(string requestId, string tokenId) =>
        $"redaction|{requestId}|{tokenId}";

    /// <summary>解析配置字符串，未知值回落到 mask（fail-safe 而非 fail-open）。</summary>
    public static RedactionMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "placeholder" => RedactionMode.Placeholder,
        "tokenize" => RedactionMode.Tokenize,
        _ => RedactionMode.Mask
    };
}
