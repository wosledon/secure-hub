using System.Text.RegularExpressions;
using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Guardrails;

public sealed record Detection(
    string DetectorId,
    string RuleId,
    int MatchCount,
    string FieldPath,
    string? SampleHash,
    GuardrailSeverity Severity,
    IReadOnlyList<string> MatchedValues);

public sealed record GuardrailVerdict(
    GuardrailAction Action,
    IReadOnlyList<Detection> Detections,
    bool FailedClosed,
    string? FailureDetectorId)
{
    public static GuardrailVerdict Allow() => new(GuardrailAction.Allow, [], false, null);
}

public interface IGuardrailDetector
{
    string Id { get; }
    GuardrailDirection Direction { get; }
    bool RequiresExternalCall { get; }
    ValueTask<IReadOnlyList<Detection>> DetectAsync(string text, string fieldPath, CancellationToken ct = default);
}

/// <summary>动作合成：Block &gt; Redact &gt; Alert &gt; Allow（PRD §7.2.4）。</summary>
public static class GuardrailDecision
{
    public static GuardrailAction Merge(IReadOnlyList<Detection> detections)
    {
        if (detections.Count == 0) return GuardrailAction.Allow;
        if (detections.Any(d => d.Severity == GuardrailSeverity.Critical)) return GuardrailAction.Block;
        if (detections.Any(d => d.Severity == GuardrailSeverity.Warning)) return GuardrailAction.Redact;
        return GuardrailAction.Alert;
    }

    public static GuardrailVerdict Build(IReadOnlyList<Detection> detections) =>
        new(Merge(detections), detections, false, null);
}

public static partial class PiiDetectors
{
    private static readonly int[] IdCardWeights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
    private static readonly char[] IdCardChecks = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2'];

    [GeneratedRegex(@"(?<!\d)(\d{17}[\dXx])(?!\d)", RegexOptions.Compiled)]
    private static partial Regex IdCardRegex();

    [GeneratedRegex(@"(?<!\d)(1[3-9]\d{9})(?!\d)", RegexOptions.Compiled)]
    private static partial Regex MobileRegex();

    [GeneratedRegex(@"(?<![\w.-])([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?![\w.-])", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(\d{16,19})(?!\d)", RegexOptions.Compiled)]
    private static partial Regex BankCardRegex();

    public static bool IsValidChineseIdCard(string candidate)
    {
        if (candidate.Length != 18) return false;

        var sum = 0;
        for (var i = 0; i < 17; i++)
        {
            if (!char.IsAsciiDigit(candidate[i])) return false;
            sum += (candidate[i] - '0') * IdCardWeights[i];
        }

        var expected = IdCardChecks[sum % 11];
        return char.ToUpperInvariant(candidate[17]) == expected;
    }

    public static bool IsValidLuhn(string digits)
    {
        if (digits.Length is < 12 or > 19) return false;

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(digits[i])) return false;
            var value = digits[i] - '0';
            if (alternate)
            {
                value *= 2;
                if (value > 9) value -= 9;
            }

            sum += value;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static IReadOnlyList<Detection> Scan(string text, string fieldPath)
    {
        var detections = new List<Detection>();

        var idCards = IdCardRegex().Matches(text)
            .Select(m => m.Groups[1].Value)
            .Where(IsValidChineseIdCard)
            .ToList();
        if (idCards.Count > 0)
        {
            detections.Add(new Detection(
                "pii.cn_idcard", "pii.cn_idcard", idCards.Count, fieldPath,
                SampleHash(idCards[0]), GuardrailSeverity.Critical, idCards));
        }

        var mobiles = MobileRegex().Matches(text).Select(m => m.Groups[1].Value).ToList();
        if (mobiles.Count > 0)
        {
            detections.Add(new Detection(
                "pii.cn_mobile", "pii.cn_mobile", mobiles.Count, fieldPath,
                SampleHash(mobiles[0]), GuardrailSeverity.Warning, mobiles));
        }

        var cards = BankCardRegex().Matches(text)
            .Select(m => m.Groups[1].Value)
            .Where(IsValidLuhn)
            .ToList();
        if (cards.Count > 0)
        {
            detections.Add(new Detection(
                "pii.bank_card", "pii.bank_card", cards.Count, fieldPath,
                SampleHash(cards[0]), GuardrailSeverity.Critical, cards));
        }

        var emails = EmailRegex().Matches(text).Select(m => m.Groups[1].Value).ToList();
        if (emails.Count > 0)
        {
            detections.Add(new Detection(
                "pii.email", "pii.email", emails.Count, fieldPath,
                SampleHash(emails[0]), GuardrailSeverity.Info, emails));
        }

        return detections;
    }

    internal static string SampleHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }
}

public sealed class PiiDetector : IGuardrailDetector
{
    public string Id => "pii";
    public GuardrailDirection Direction => GuardrailDirection.Inbound;
    public bool RequiresExternalCall => false;

    public ValueTask<IReadOnlyList<Detection>> DetectAsync(string text, string fieldPath, CancellationToken ct = default) =>
        ValueTask.FromResult(PiiDetectors.Scan(text, fieldPath));
}

public static partial class SecretDetectors
{
    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"(?<![\w-])(eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,})(?![\w-])", RegexOptions.Compiled)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"[A-Za-z0-9+/=_-]{20,}", RegexOptions.Compiled)]
    private static partial Regex CandidateRegex();

    private static readonly string[] KnownPrefixes =
        ["sk-", "AKIA", "AIza", "ghp_", "gho_", "xoxb-", "glpat-"];

    private static readonly string[] PlaceholderHints =
        ["your-key", "your_key", "placeholder", "example", "xxxx", "dummy", "changeme"];

    public static bool LooksLikeSecret(string candidate)
    {
        if (candidate.Length < 20) return false;
        if (PlaceholderHints.Any(h => candidate.Contains(h, StringComparison.OrdinalIgnoreCase))) return false;

        var hasUpper = candidate.Any(char.IsUpper);
        var hasLower = candidate.Any(char.IsLower);
        var hasDigit = candidate.Any(char.IsAsciiDigit);
        if (!hasDigit || !(hasUpper || hasLower)) return false;

        return ShannonEntropy(candidate) >= 4.5;
    }

    public static double ShannonEntropy(string value)
    {
        if (value.Length == 0) return 0;

        Span<int> counts = stackalloc int[256];
        foreach (var c in value)
        {
            counts[(byte)c]++;
        }

        var entropy = 0d;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            var p = count / (double)value.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    public static IReadOnlyList<Detection> Scan(string text, string fieldPath)
    {
        var detections = new List<Detection>();

        if (PrivateKeyRegex().IsMatch(text))
        {
            detections.Add(new Detection(
                "secret.private_key", "secret.private_key", 1, fieldPath, null,
                GuardrailSeverity.Critical, ["<private-key>"]));
        }

        var jwts = JwtRegex().Matches(text).Select(m => m.Groups[1].Value).ToList();
        if (jwts.Count > 0)
        {
            detections.Add(new Detection(
                "secret.jwt", "secret.jwt", jwts.Count, fieldPath,
                PiiDetectors.SampleHash(jwts[0]), GuardrailSeverity.Warning, ["<jwt>"]));
        }

        var suspicious = CandidateRegex().Matches(text)
            .Select(m => m.Value)
            .Where(v => KnownPrefixes.Any(p => v.StartsWith(p, StringComparison.Ordinal)) || LooksLikeSecret(v))
            .ToList();

        if (suspicious.Count > 0)
        {
            detections.Add(new Detection(
                "secret.generic", "secret.generic", suspicious.Count, fieldPath,
                PiiDetectors.SampleHash(suspicious[0]), GuardrailSeverity.Critical, ["<secret>"]));
        }

        return detections;
    }
}

public sealed class SecretDetector : IGuardrailDetector
{
    public string Id => "secret";
    public GuardrailDirection Direction => GuardrailDirection.Inbound;
    public bool RequiresExternalCall => false;

    public ValueTask<IReadOnlyList<Detection>> DetectAsync(string text, string fieldPath, CancellationToken ct = default) =>
        ValueTask.FromResult(SecretDetectors.Scan(text, fieldPath));
}
