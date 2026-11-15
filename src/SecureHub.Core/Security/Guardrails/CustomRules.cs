using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Guardrails;

/// <summary>管理员自定义敏感词规则（PRD §7.5 自定义词库）；detector id 恒为 <c>custom.{key}</c>。</summary>
/// <remarks>
/// <see cref="Severity"/> 沿用内置合成语义：<c>Info</c>→Alert、<c>Warning</c>→Redact、<c>Critical</c>→Block。
/// </remarks>
public sealed record CustomRule
{
    public string Key { get; init; } = string.Empty;
    public string Pattern { get; init; } = string.Empty;
    /// <summary>"keyword"（字面量）或 "regex"（正则）。</summary>
    public string MatchType { get; init; } = "keyword";
    public bool IgnoreCase { get; init; } = true;
    public GuardrailSeverity Severity { get; init; } = GuardrailSeverity.Warning;
    public GuardrailDirection Direction { get; init; } = GuardrailDirection.Inbound;
    public bool Enabled { get; init; } = true;
    public string? Sample { get; init; }
    public string? Description { get; init; }

    [JsonIgnore]
    public string DetectorId => $"custom.{Key}";

    [JsonIgnore]
    public bool IsRegex => string.Equals(MatchType, "regex", StringComparison.OrdinalIgnoreCase);
}

/// <summary>自定义规则的 JSON 序列化选项（存储与读取共用；枚举按小写字符串读写）。</summary>
public static class CustomRuleJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

/// <summary>规则校验（保存前调用，返回机器可读错误码；null 表示通过）。</summary>
public static class CustomRuleValidation
{
    public const int MaxRules = 100;
    public const int MaxPatternLength = 500;
    public const int MaxDescriptionLength = 500;
    /// <summary>单条规则单次匹配的正则超时（防 ReDoS，PRD §7.6）。</summary>
    public static TimeSpan RegexTimeout { get; } = TimeSpan.FromMilliseconds(100);

    public static readonly Regex KeyPattern =
        new("^[a-z][a-z0-9_]{1,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>校验单条规则；<paramref name="isDuplicateKey"/> 为 true 时拒绝重复 key。</summary>
    public static string? Validate(CustomRule rule, bool isDuplicateKey = false)
    {
        if (string.IsNullOrWhiteSpace(rule.Key) || !KeyPattern.IsMatch(rule.Key)) return "rule_key_invalid";
        if (isDuplicateKey) return "rule_key_duplicate";
        if (string.IsNullOrWhiteSpace(rule.Pattern)) return "rule_pattern_required";
        if (rule.Pattern.Length > MaxPatternLength) return "rule_pattern_too_long";
        if (rule.Description is { Length: > MaxDescriptionLength }) return "rule_description_too_long";
        if (rule.Sample is { Length: > MaxPatternLength }) return "rule_sample_too_long";

        var matchType = rule.MatchType.Trim().ToLowerInvariant();
        if (matchType is not ("keyword" or "regex")) return "rule_match_type_invalid";

        if (matchType == "regex")
        {
            // 编译不过的正则直接拒绝，避免落到快照里被静默跳过。
            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return "rule_pattern_invalid";
            }
        }

        return null;
    }
}

/// <summary>已编译规则（快照单元）；正则编译失败的条目在编译期被丢弃，绝不抛出。</summary>
public sealed record CompiledCustomRule(CustomRule Rule, Regex? Regex);

/// <summary>把规则集合编译为快照：禁用项与编译失败项跳过。</summary>
public static class CustomRuleCompiler
{
    public static IReadOnlyList<CompiledCustomRule> Compile(IEnumerable<CustomRule> rules)
    {
        var compiled = new List<CompiledCustomRule>();
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            if (rule.IsRegex)
            {
                Regex? regex;
                try
                {
                    var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
                    if (rule.IgnoreCase) options |= RegexOptions.IgnoreCase;
                    regex = new Regex(rule.Pattern, options, CustomRuleValidation.RegexTimeout);
                }
                catch (ArgumentException)
                {
                    continue; // 非法正则：保留其余规则，整体快照可用。
                }

                compiled.Add(new CompiledCustomRule(rule, regex));
            }
            else
            {
                compiled.Add(new CompiledCustomRule(rule, null));
            }
        }

        return compiled;
    }
}

public static class CustomRuleExtensions
{
    /// <summary>并入检测器目录视图（<c>custom</c> 分类）。</summary>
    public static DetectorDescriptor ToDescriptor(this CustomRule rule) =>
        new(rule.DetectorId, "custom", rule.Severity, rule.Sample ?? string.Empty);
}
