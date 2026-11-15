using System.Text.RegularExpressions;
using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Guardrails;

/// <summary>
/// 自定义规则检测器：按方向实例化（管道按 <see cref="IGuardrailDetector.Direction"/> 过滤），
/// 从注入的快照读取规则；命中产出 <c>custom.{key}</c> 检测，供策略条件按 id 引用。
/// </summary>
public sealed class CustomRuleDetector : IGuardrailDetector
{
    /// <summary>单条规则最多收集的命中值数量（防超大文本内存放大）。</summary>
    private const int MaxMatchedValues = 100;

    private readonly IReadOnlyList<CompiledCustomRule> _snapshot;

    public CustomRuleDetector(IReadOnlyList<CompiledCustomRule> snapshot, GuardrailDirection direction)
    {
        _snapshot = snapshot;
        Direction = direction;
    }

    public string Id => "custom";

    public GuardrailDirection Direction { get; }

    public bool RequiresExternalCall => false;

    public ValueTask<IReadOnlyList<Detection>> DetectAsync(
        string text, string fieldPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return ValueTask.FromResult<IReadOnlyList<Detection>>([]);

        List<Detection>? found = null;
        foreach (var compiled in _snapshot)
        {
            ct.ThrowIfCancellationRequested();
            var rule = compiled.Rule;
            if (rule.Direction != Direction) continue;

            var (count, values) = compiled.Regex is null
                ? MatchKeyword(rule, text)
                : MatchRegex(compiled.Regex, text);

            if (count == 0 || values.Count == 0) continue;

            (found ??= []).Add(new Detection(
                rule.DetectorId,
                rule.DetectorId,
                count,
                fieldPath,
                SampleHash(values[0]),
                rule.Severity,
                values));
        }

        return ValueTask.FromResult<IReadOnlyList<Detection>>(found ?? []);
    }

    /// <summary>返回（真实命中数, 收集的命中值，最多 <see cref="MaxMatchedValues"/> 个）。</summary>
    private static (int Count, List<string> Values) MatchKeyword(CustomRule rule, string text)
    {
        var comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var values = new List<string>();
        var count = 0;
        var index = 0;
        while (index <= text.Length - rule.Pattern.Length)
        {
            var hit = text.IndexOf(rule.Pattern, index, comparison);
            if (hit < 0) break;
            count++;
            if (values.Count < MaxMatchedValues)
                values.Add(text.Substring(hit, rule.Pattern.Length));
            index = hit + rule.Pattern.Length;
        }

        return (count, values);
    }

    /// <summary>返回（真实命中数, 收集的命中值，最多 <see cref="MaxMatchedValues"/> 个）。</summary>
    private static (int Count, List<string> Values) MatchRegex(Regex regex, string text)
    {
        var values = new List<string>();
        var count = 0;
        try
        {
            foreach (Match match in regex.Matches(text))
            {
                if (match.Value.Length == 0) continue;
                count++;
                if (values.Count < MaxMatchedValues) values.Add(match.Value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // 超时视为该规则此文本未命中（保存期已限定超时预算，见 CustomRuleValidation.RegexTimeout）。
        }

        return (count, values);
    }

    private static string SampleHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
