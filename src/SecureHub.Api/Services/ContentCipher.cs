using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureHub.Core.Options;

namespace SecureHub.Api.Services;

/// <summary>
/// 内容加密：AES-256-GCM，AAD 绑定 <c>requestId|direction|variant</c>（security.md I3）。
/// 单机版用一个本地 KEK 文件；生产可替换为 KMS 包裹的 DEK 轮换。
/// </summary>
public sealed class ContentCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _key;
    private readonly ILogger<ContentCipher> _logger;

    public ContentCipher(IOptions<SecureHubOptions> options, ILogger<ContentCipher> logger)
    {
        _logger = logger;
        var dataDirectory = options.Value.Security.DataDirectory;
        Directory.CreateDirectory(dataDirectory);

        var keyPath = Path.Combine(dataDirectory, "content.key");
        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length != KeySize)
            {
                throw new InvalidOperationException(
                    $"Content key at '{keyPath}' must be {KeySize} bytes, found {existing.Length}.");
            }

            _key = existing;
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(keyPath, _key);
            _logger.LogWarning(
                "Generated new content encryption key at {Path}. Back it up: losing it makes stored content unreadable.",
                keyPath);
        }
    }

    public string DekId { get; } = "k1";

    public (byte[] Cipher, byte[] Nonce, byte[] Tag) Seal(string plaintext, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(nonce, plainBytes, cipher, tag, Encoding.UTF8.GetBytes(aad));

        return (cipher, nonce, tag);
    }

    public string Open(byte[] cipher, byte[] nonce, byte[] tag, string aad)
    {
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(aad));
        return Encoding.UTF8.GetString(plain);
    }

    public static string BuildAad(string requestId, string direction, string variant) =>
        $"{requestId}|{direction.ToLowerInvariant()}|{variant.ToLowerInvariant()}";

    public static string HashContent(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
