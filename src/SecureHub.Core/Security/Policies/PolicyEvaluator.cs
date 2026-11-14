using System.Text.Json.Serialization;
using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Policies;

public sealed class PolicyScope
{
    public string[] Teams { get; set; } = [];
    public string[] Apps { get; set; } = [];
    public string[] Models { get; set; } = [];
}

public sealed class DetectorRequirement
{
    public string Detector { get; set; } = string.Empty;
    public int MinMatches { get; set; } = 1;
}

public sealed class PolicyCondition
{
    /// <summary>任一条件满足即可（与 all 互斥，同时存在时先 all 后 any）。</summary>
    public DetectorRequirement[] Any { get; set; } = [];

    public DetectorRequirement[] All { get; set; } = [];
}

public sealed class PolicyThen
{
    public string Action { get; set; } = "block";
    public string Reason { get; set; } = "policy_matched";
    public string? RuleId { get; set; }
}

public sealed class PolicyRollout
{
    public int Percent { get; set; } = 100;
}

/// <summary>编译后的规则：不解释执行字符串表达式，条件为结构化模型（PRD §7.3.1）。</summary>
public sealed record CompiledPolicy(
    string PolicyKey,
    int Version,
    int Priority,
    bool Enabled,
    PolicyScope Scope,
    PolicyCondition Condition,
    GuardrailAction Action,
    string Reason,
    string? RuleId,
    int RolloutPercent,
    string OnError);

public sealed record PolicyEvalContext(
    Guid? AppId,
    Guid? TeamId,
    string? Model,
    IReadOnlyCollection<string> Detectors,
    int Bucket,
    TimeOnly Now);

public sealed record PolicyHit(
    string PolicyKey,
    int Version,
    string? RuleId,
    GuardrailAction Action,
    string Reason);

public sealed record PolicyDecision(
    GuardrailAction Action,
    IReadOnlyList<PolicyHit> Hits)
{
    public static readonly PolicyDecision Allow = new(GuardrailAction.Allow, []);
    public bool Blocked => Action == GuardrailAction.Block;
}

public static class PolicyEvaluator
{
    public static PolicyDecision Evaluate(
        IReadOnlyList<CompiledPolicy> policies,
        PolicyEvalContext context)
    {
        var ordered = policies
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.PolicyKey, StringComparer.Ordinal)
            .ToArray();

        var hits = new List<PolicyHit>();

        foreach (var policy in ordered)
        {
            if (!ScopeMatches(policy.Scope, context)) continue;
            if (policy.RolloutPercent < 100 && context.Bucket >= policy.RolloutPercent) continue;
            if (!ConditionMatches(policy.Condition, context.Detectors)) continue;

            hits.Add(new PolicyHit(policy.PolicyKey, policy.Version, policy.RuleId, policy.Action, policy.Reason));
        }

        if (hits.Count == 0) return PolicyDecision.Allow;

        // block > redact > alert；同级别取第一条（已按优先级排序）
        var action = hits.Max(h => h.Action);
        return new PolicyDecision(action, hits);
    }

    private static bool ScopeMatches(PolicyScope scope, PolicyEvalContext context)
    {
        if (!MatchesAny(scope.Teams, context.TeamId?.ToString())) return false;
        if (!MatchesAny(scope.Apps, context.AppId?.ToString())) return false;
        if (!MatchesAny(scope.Models, context.Model)) return false;
        return true;
    }

    /// <summary>空数组表示"不限制"；通配只支持前后缀 *。</summary>
    private static bool MatchesAny(string[] patterns, string? value)
    {
        if (patterns.Length == 0) return true;
        if (string.IsNullOrEmpty(value)) return false;

        foreach (var pattern in patterns)
        {
            if (WildcardMatch(pattern, value)) return true;
        }

        return false;
    }

    public static bool WildcardMatch(string pattern, string value)
    {
        if (pattern == "*") return true;

        if (pattern.StartsWith('*') && pattern.EndsWith('*') && pattern.Length > 2)
        {
            return value.Contains(pattern[1..^1], StringComparison.Ordinal);
        }

        if (pattern.StartsWith('*'))
        {
            return value.EndsWith(pattern[1..], StringComparison.Ordinal);
        }

        if (pattern.EndsWith('*'))
        {
            return value.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }

        return string.Equals(pattern, value, StringComparison.Ordinal);
    }

    private static bool ConditionMatches(PolicyCondition condition, IReadOnlyCollection<string> detectors)
    {
        var hasAll = condition.All.Length > 0;
        var hasAny = condition.Any.Length > 0;

        // 无条件 = 仅按 scope 匹配（例如"该应用的所有请求都脱敏"）。
        // 空 scope + 空 when 会命中全部请求，属于显式配置错误，由管理端校验提示。
        if (!hasAll && !hasAny) return true;

        if (hasAll && !condition.All.All(requirement => Satisfied(requirement, detectors))) return false;

        if (hasAny) return condition.Any.Any(requirement => Satisfied(requirement, detectors));

        return true;
    }

    private static bool Satisfied(DetectorRequirement requirement, IReadOnlyCollection<string> detectors) =>
        requirement.MinMatches <= 0 || detectors.Contains(requirement.Detector, StringComparer.Ordinal);
}

public static class PolicyActionParser
{
    public static GuardrailAction Parse(string? action) =>
        action?.Trim().ToLowerInvariant() switch
        {
            "allow" => GuardrailAction.Allow,
            "alert" => GuardrailAction.Alert,
            "redact" => GuardrailAction.Redact,
            "block" => GuardrailAction.Block,
            _ => GuardrailAction.Block
        };
}
