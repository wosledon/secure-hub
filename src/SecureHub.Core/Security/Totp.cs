using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SecureHub.Core.Security;

/// <summary>TOTP（RFC 6238）：SHA-1、6 位、30 秒步长。</summary>
public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;

    public static string GenerateSecret(int bytes = 20)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Base32Encode(buffer);
    }

    public static bool Verify(string base32Secret, string? code, int allowedDrift = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code)) return false;
        if (code.Trim().Length != Digits || !code.Trim().All(char.IsAsciiDigit)) return false;

        var key = Base32Decode(base32Secret);
        if (key.Length == 0) return false;

        var expected = code.Trim();
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

        for (var drift = -allowedDrift; drift <= allowedDrift; drift++)
        {
            var candidate = Compute(key, counter + drift);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(candidate),
                    Encoding.ASCII.GetBytes(expected)))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildOtpAuthUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    private static string Compute(byte[] key, long counter)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, counter);

        var hash = HMACSHA1.HashData(key, buffer);
        var offset = hash[^1] & 0x0F;

        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return output.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var normalized = input.Trim().Replace("=", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length == 0) return [];

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var c in normalized)
        {
            var index = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (index < 0) return [];

            buffer = (buffer << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
