using System.Security.Cryptography;
using System.Text;

namespace SecureHub.Core.Security;

/// <summary>解析后的虚拟密钥。</summary>
public sealed record ParsedVirtualKey(string Environment, string KeyId, string Secret);

/// <summary>
/// 虚拟密钥格式：<c>sk-sh-{env}-{keyId}-{secret}</c>。
/// 与 OpenAI 生态习惯一致，客户端无需特殊处理。
/// </summary>
public static class VirtualKeyFormat
{
    public const string Scheme = "sk-sh";
    public const string DefaultEnvironment = "live";
    public const int KeyIdLength = 8;
    public const int SecretBytes = 32;

    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static (string FullKey, ParsedVirtualKey Parsed) Generate(
        string environment = DefaultEnvironment,
        int secretBytes = SecretBytes)
    {
        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyIdLength / 2)).ToLowerInvariant();
        var secretBytesBuffer = RandomNumberGenerator.GetBytes(secretBytes);
        var secret = ToBase62(secretBytesBuffer);

        var full = $"{Scheme}-{environment}-{keyId}-{secret}";
        return (full, new ParsedVirtualKey(environment, keyId, secret));
    }

    public static bool TryParse(string? raw, out ParsedVirtualKey parsed)
    {
        parsed = new ParsedVirtualKey(string.Empty, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Trim().Split('-');
        // sk | sh | env | keyId | secret
        if (parts.Length != 5) return false;
        if (!string.Equals(parts[0], "sk", StringComparison.Ordinal)) return false;
        if (!string.Equals(parts[1], "sh", StringComparison.Ordinal)) return false;

        var environment = parts[2];
        var keyId = parts[3];
        var secret = parts[4];

        if (environment.Length is 0 or > 16) return false;
        if (keyId.Length != KeyIdLength || !IsHex(keyId)) return false;
        if (secret.Length < 16 || secret.Length > 128) return false;

        parsed = new ParsedVirtualKey(environment, keyId, secret);
        return true;
    }

    /// <summary>只存 SHA-256(secret)。</summary>
    public static byte[] HashSecret(string secret) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(secret));

    public static string Mask(string keyId) => $"{Scheme}-••••-{keyId}-••••";

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex) return false;
        }

        return true;
    }

    private static string ToBase62(ReadOnlySpan<byte> data)
    {
        // 每 8 字节一组转成 ulong，再按 62 进制输出（保留前导零位以维持长度可预期）。
        var sb = new StringBuilder(data.Length * 2);
        var buffer = new byte[8];
        for (var offset = 0; offset < data.Length; offset += 8)
        {
            var chunk = data.Slice(offset, Math.Min(8, data.Length - offset));
            buffer.AsSpan().Clear();
            chunk.CopyTo(buffer);
            var value = BitConverter.ToUInt64(buffer);
            var digits = new char[11];
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                digits[i] = Base62[(int)(value % 62)];
                value /= 62;
            }

            sb.Append(digits);
        }

        return sb.ToString();
    }
}
