using SecureHub.Core.Compression;
using SecureHub.Core.Domain;
using SecureHub.Core.Security.Guardrails;

namespace SecureHub.Core.Tests;

public class PiiDetectorTests
{
    [Theory]
    [InlineData("11010519491231002X", true)]
    [InlineData("110105194912310021", false)]
    [InlineData("000000000000000000", false)]
    [InlineData("11010519491231002", false)]
    public void Validates_chinese_id_card_checksum(string candidate, bool expected)
    {
        Assert.Equal(expected, PiiDetectors.IsValidChineseIdCard(candidate));
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("1234", false)]
    public void Validates_luhn(string digits, bool expected)
    {
        Assert.Equal(expected, PiiDetectors.IsValidLuhn(digits));
    }

    [Fact]
    public void Scans_id_card_and_mobile()
    {
        var text = "客户身份证 11010519491231002X，电话 13812345678。";
        var detections = PiiDetectors.Scan(text, "messages[0].content");

        Assert.Contains(detections, d => d.DetectorId == "pii.cn_idcard" && d.Severity == GuardrailSeverity.Critical);
        Assert.Contains(detections, d => d.DetectorId == "pii.cn_mobile");
    }

    [Fact]
    public void Invalid_id_card_is_not_reported()
    {
        var detections = PiiDetectors.Scan("编号 110105194912310021", "f");
        Assert.DoesNotContain(detections, d => d.DetectorId == "pii.cn_idcard");
    }

    [Fact]
    public void Sample_hash_is_not_plaintext()
    {
        var detections = PiiDetectors.Scan("电话 13812345678", "f");
        var mobile = detections.Single(d => d.DetectorId == "pii.cn_mobile");
        Assert.DoesNotContain("13812345678", mobile.SampleHash);
    }
}

public class SecretDetectorTests
{
    [Fact]
    public void Detects_private_key_block()
    {
        var text = "-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----";
        var detections = SecretDetectors.Scan(text, "f");
        Assert.Contains(detections, d => d.DetectorId == "secret.private_key");
    }

    [Fact]
    public void Detects_high_entropy_secret()
    {
        var detections = SecretDetectors.Scan("key=Xk9mQ2pL7wR4tY8uZ1vB3nC5", "f");
        Assert.Contains(detections, d => d.DetectorId == "secret.generic");
    }

    [Fact]
    public void Does_not_flag_plain_english()
    {
        var detections = SecretDetectors.Scan("The quick brown fox jumps over the lazy dog", "f");
        Assert.Empty(detections);
    }

    [Fact]
    public void Does_not_flag_placeholders()
    {
        Assert.Empty(SecretDetectors.Scan("api_key=your-key-here-please-change", "f"));
    }

    [Fact]
    public void Entropy_distinguishes_random_from_repetitive()
    {
        var random = SecretDetectors.ShannonEntropy("Xk9mQ2pL7wR4tY8uZ1vB3nC5");
        var repetitive = SecretDetectors.ShannonEntropy("aaaaaaaaaaaaaaaaaaaaaaaa");
        Assert.True(random > repetitive);
    }
}

public class GuardrailDecisionTests
{
    private static Detection Detection(GuardrailSeverity severity) =>
        new("d", "r", 1, "f", null, severity, ["v"]);

    [Fact]
    public void Block_wins_when_any_critical()
    {
        var action = GuardrailDecision.Merge(
        [
            Detection(GuardrailSeverity.Info),
            Detection(GuardrailSeverity.Warning),
            Detection(GuardrailSeverity.Critical)
        ]);

        Assert.Equal(GuardrailAction.Block, action);
    }

    [Fact]
    public void Empty_means_allow()
    {
        Assert.Equal(GuardrailAction.Allow, GuardrailDecision.Merge([]));
    }
}

public class RedactorTests
{
    [Fact]
    public void Masks_mobile_keeping_prefix_and_suffix()
    {
        Assert.Equal("138****5678", Redactor.Mask("13812345678"));
    }

    [Fact]
    public void Masks_email_keeping_domain()
    {
        var masked = Redactor.Mask("alice@example.com");
        Assert.StartsWith("a***", masked);
        Assert.EndsWith("@example.com", masked);
    }

    [Fact]
    public void Replaces_matched_values()
    {
        var text = "id=11010519491231002X";
        var detections = PiiDetectors.Scan(text, "f");
        var (redacted, count) = Redactor.Replace(text, detections, mask: true);

        Assert.Equal(1, count);
        Assert.DoesNotContain("11010519491231002X", redacted);
    }
}

public class CompressionTests
{
    [Fact]
    public void Normalizes_crlf_and_blank_lines()
    {
        var input = "a\r\n\r\n\r\n\r\nb   ";
        var output = CompressionPipeline.NormalizeWhitespace(input);
        Assert.Equal("a\n\nb", output);
    }

    [Fact]
    public void Collapses_repeated_lines()
    {
        var input = string.Join('\n', Enumerable.Repeat("---- log line ----", 5));
        var output = CompressionPipeline.CollapseRepetition(input, 3);
        Assert.Contains("×5", output);
        Assert.True(output.Length < input.Length);
    }

    [Fact]
    public void Does_not_collapse_lines_with_numbers()
    {
        var input = string.Join('\n', Enumerable.Repeat("error code 500", 5));
        var output = CompressionPipeline.CollapseRepetition(input, 3);
        Assert.DoesNotContain("×", output);
    }

    [Fact]
    public void Compression_keeps_all_numbers()
    {
        var input = "line 1\nline 2\nline 3\nline 4\nline 5\n";
        var result = CompressionPipeline.Compress(input, new CompressionSettings());

        Assert.Null(IntegrityGuard.Check(input, result.Text));
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(result.Text, @"\d+").Count);
    }

    [Fact]
    public void Low_savings_is_rejected()
    {
        var input = "short";
        var result = CompressionPipeline.Compress(input, new CompressionSettings());
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void Integrity_guard_detects_lost_numbers()
    {
        Assert.Equal("numbers-lost", IntegrityGuard.Check("a 1 b 2", "a b"));
    }

    [Fact]
    public void Integrity_guard_detects_unbalanced_brackets()
    {
        Assert.Equal("unbalanced-brackets", IntegrityGuard.Check("(a)", "(a"));
    }
}
