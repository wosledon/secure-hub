using System.Text.Json;
using SecureHub.Core.Domain;
using SecureHub.Core.Security.Guardrails;

namespace SecureHub.Core.Tests;

public class CustomRuleValidationTests
{
    private static CustomRule Rule(Func<CustomRule, CustomRule>? mutate = null)
    {
        var rule = new CustomRule { Key = "internal_token", Pattern = "ACME-SECRET" };
        return mutate is null ? rule : mutate(rule);
    }

    [Fact]
    public void Accepts_valid_keyword_rule()
    {
        Assert.Null(CustomRuleValidation.Validate(Rule()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER_CASE")]
    [InlineData("1leading_digit")]
    [InlineData("-dash")]
    [InlineData("k_way_too_long_key_that_exceeds_the_sixty_four_character_limit_ok_and_more")]
    public void Rejects_invalid_key(string key)
    {
        Assert.Equal("rule_key_invalid", CustomRuleValidation.Validate(Rule(r => r with { Key = key })));
    }

    [Fact]
    public void Rejects_duplicate_key()
    {
        Assert.Equal("rule_key_duplicate", CustomRuleValidation.Validate(Rule(), isDuplicateKey: true));
    }

    [Fact]
    public void Rejects_blank_pattern()
    {
        Assert.Equal("rule_pattern_required", CustomRuleValidation.Validate(Rule(r => r with { Pattern = "  " })));
    }

    [Fact]
    public void Rejects_oversized_pattern()
    {
        var pattern = new string('a', CustomRuleValidation.MaxPatternLength + 1);
        Assert.Equal("rule_pattern_too_long", CustomRuleValidation.Validate(Rule(r => r with { Pattern = pattern })));
    }

    [Fact]
    public void Rejects_oversized_description()
    {
        var description = new string('a', CustomRuleValidation.MaxDescriptionLength + 1);
        Assert.Equal("rule_description_too_long", CustomRuleValidation.Validate(Rule(r => r with { Description = description })));
    }

    [Theory]
    [InlineData("word")]
    [InlineData("")]
    public void Rejects_unknown_match_type(string matchType)
    {
        Assert.Equal("rule_match_type_invalid", CustomRuleValidation.Validate(Rule(r => r with { MatchType = matchType })));
    }

    [Fact]
    public void Rejects_uncompilable_regex()
    {
        Assert.Equal("rule_pattern_invalid",
            CustomRuleValidation.Validate(Rule(r => r with { MatchType = "regex", Pattern = "(unclosed" })));
    }

    [Fact]
    public void Match_type_is_case_insensitive()
    {
        Assert.Null(CustomRuleValidation.Validate(Rule(r => r with { MatchType = "REGEX", Pattern = "^ok$" })));
    }

    [Fact]
    public void Detector_id_prefixes_key()
    {
        Assert.Equal("custom.internal_token", Rule().DetectorId);
    }
}

public class CustomRuleCompilerTests
{
    [Fact]
    public void Skips_disabled_and_blank_rules()
    {
        var compiled = CustomRuleCompiler.Compile([
            new CustomRule { Key = "off", Pattern = "x", Enabled = false },
            new CustomRule { Key = "blank", Pattern = "  " },
            new CustomRule { Key = "on", Pattern = "x" }
        ]);

        Assert.Single(compiled);
        Assert.Equal("custom.on", compiled[0].Rule.DetectorId);
    }

    [Fact]
    public void Drops_uncompilable_regex_but_keeps_the_rest()
    {
        var compiled = CustomRuleCompiler.Compile([
            new CustomRule { Key = "bad", MatchType = "regex", Pattern = "(unclosed" },
            new CustomRule { Key = "good", MatchType = "regex", Pattern = "^ok$" }
        ]);

        Assert.Single(compiled);
        Assert.Equal("custom.good", compiled[0].Rule.DetectorId);
        Assert.NotNull(compiled[0].Regex);
    }

    [Fact]
    public void Keyword_rules_have_no_regex_instance()
    {
        var compiled = CustomRuleCompiler.Compile([new CustomRule { Key = "kw", Pattern = "literal" }]);
        Assert.Null(compiled[0].Regex);
    }
}

public class CustomRuleJsonTests
{
    [Fact]
    public void Round_trips_enums_as_lowercase_strings()
    {
        var rule = new CustomRule
        {
            Key = "leak",
            MatchType = "regex",
            Pattern = "AKIA[0-9A-Z]+",
            Severity = GuardrailSeverity.Critical,
            Direction = GuardrailDirection.Outbound
        };

        var json = JsonSerializer.Serialize(rule, CustomRuleJson.Options);

        Assert.Contains("\"severity\":\"critical\"", json);
        Assert.Contains("\"direction\":\"outbound\"", json);
        Assert.DoesNotContain("Critical", json);

        var back = JsonSerializer.Deserialize<CustomRule>(json, CustomRuleJson.Options);
        Assert.NotNull(back);
        Assert.Equal(GuardrailSeverity.Critical, back!.Severity);
        Assert.Equal(GuardrailDirection.Outbound, back.Direction);
        Assert.Equal("regex", back.MatchType);
    }
}

public class CustomRuleDetectorTests
{
    private static CustomRuleDetector Create(params CustomRule[] rules) =>
        new(CustomRuleCompiler.Compile(rules), GuardrailDirection.Inbound);

    private static CustomRule Keyword(string key, string pattern, bool ignoreCase = true,
        GuardrailDirection direction = GuardrailDirection.Inbound) => new()
    {
        Key = key,
        Pattern = pattern,
        IgnoreCase = ignoreCase,
        Direction = direction
    };

    [Fact]
    public async Task Empty_text_yields_nothing()
    {
        var detector = Create(Keyword("kw", "secret"));
        Assert.Empty(await detector.DetectAsync("", "f"));
    }

    [Fact]
    public async Task Matches_keyword_and_reports_custom_detector_id()
    {
        var detector = Create(Keyword("internal_token", "ACME-SECRET"));

        var hits = await detector.DetectAsync("prefix ACME-SECRET suffix", "messages[0].content");

        var hit = Assert.Single(hits);
        Assert.Equal("custom.internal_token", hit.DetectorId);
        Assert.Equal("messages[0].content", hit.FieldPath);
        Assert.Equal(1, hit.MatchCount);
        Assert.Equal(GuardrailSeverity.Warning, hit.Severity);
        Assert.Contains("ACME-SECRET", hit.MatchedValues);
    }

    [Fact]
    public async Task Keyword_respects_ignore_case_flag()
    {
        var sensitive = Create(Keyword("kw", "secret", ignoreCase: false));
        Assert.Empty(await sensitive.DetectAsync("SECRET value", "f"));

        var lenient = Create(Keyword("kw", "secret", ignoreCase: true));
        Assert.Single(await lenient.DetectAsync("SECRET value", "f"));
    }

    [Fact]
    public async Task Counts_every_occurrence()
    {
        var detector = Create(Keyword("kw", "aa"));
        var hits = await detector.DetectAsync("aa aa aa", "f");

        Assert.Equal(3, Assert.Single(hits).MatchCount);
    }

    [Fact]
    public async Task Filters_by_direction()
    {
        var detector = Create(Keyword("out", "needle", direction: GuardrailDirection.Outbound));
        Assert.Empty(await detector.DetectAsync("needle here", "f"));
    }

    [Fact]
    public async Task Matches_regex_rule()
    {
        var detector = Create(new CustomRule
        {
            Key = "aws_key",
            Pattern = "AKIA[0-9A-Z]{16}",
            MatchType = "regex",
            Direction = GuardrailDirection.Inbound
        });

        var hits = await detector.DetectAsync("id=AKIA1234567890ABCDEF", "f");

        var hit = Assert.Single(hits);
        Assert.Equal("custom.aws_key", hit.DetectorId);
        Assert.Contains("AKIA1234567890ABCDEF", hit.MatchedValues);
    }

    [Fact]
    public async Task Sample_hash_is_not_the_matched_value()
    {
        var detector = Create(Keyword("kw", "TOPSECRET"));
        var hit = Assert.Single(await detector.DetectAsync("TOPSECRET", "f"));

        Assert.Equal(64, hit.SampleHash!.Length);
        Assert.DoesNotContain("TOPSECRET", hit.SampleHash);
    }

    [Fact]
    public async Task Caps_collected_values_but_keeps_true_count()
    {
        // 150 次命中（每 6 字符一个 "abcXYZ"），收集上限 100。
        var text = string.Concat(Enumerable.Repeat("abcXYZ ", 150));
        var detector = Create(Keyword("kw", "abcXYZ"));

        var hit = Assert.Single(await detector.DetectAsync(text, "f"));
        Assert.Equal(150, hit.MatchCount);
        Assert.Equal(100, hit.MatchedValues.Count);
    }

    [Fact]
    public async Task Uncompilable_regex_never_reaches_the_detector()
    {
        var detector = Create(new CustomRule { Key = "bad", MatchType = "regex", Pattern = "(unclosed" });
        Assert.Empty(await detector.DetectAsync("(unclosed", "f"));
    }

    [Fact]
    public void Descriptor_uses_custom_category()
    {
        var descriptor = new CustomRule { Key = "kw", Pattern = "x", Sample = "s" }.ToDescriptor();
        Assert.Equal("custom.kw", descriptor.Id);
        Assert.Equal("custom", descriptor.Category);
    }
}
