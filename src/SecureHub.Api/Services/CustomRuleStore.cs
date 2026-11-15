using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureHub.Core.Security.Guardrails;

namespace SecureHub.Api.Services;

/// <summary>
/// 自定义敏感词规则（PRD §7.5）：JSON 存于 <see cref="SystemSettingStore"/>，
/// 内存保留「原始规则 + 编译快照」双视图，写后立即生效；解析失败保留旧快照。
/// </summary>
public sealed class CustomRuleStore(SystemSettingStore settings, ILogger<CustomRuleStore> logger)
{
    public const string SettingKey = "guardrails.custom_rules";

    private volatile IReadOnlyList<CustomRule> _rules = [];
    private volatile IReadOnlyList<CompiledCustomRule> _compiled = [];

    /// <summary>原始规则（端点列表展示）。</summary>
    public IReadOnlyList<CustomRule> Rules => _rules;

    /// <summary>编译快照（管道检测器注入源）。</summary>
    public IReadOnlyList<CompiledCustomRule> Compiled => _compiled;

    /// <summary>策略校验：id 是否为存在的自定义规则（含内置 id 则由调用方先查内置目录）。</summary>
    public bool Contains(string detectorId) =>
        _rules.Any(rule => string.Equals(rule.DetectorId, detectorId, StringComparison.Ordinal));

    /// <summary>从设置存储重新加载；JSON 损坏时保留旧快照并记录错误。</summary>
    public void Refresh()
    {
        var raw = settings.Get(SettingKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _rules = [];
            _compiled = [];
            return;
        }

        List<CustomRule>? rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<CustomRule>>(raw, CustomRuleJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "自定义规则 JSON 损坏，保留旧快照");
            return;
        }

        var list = (rules ?? [])
            .Where(rule => CustomRuleValidation.Validate(rule) is null)
            .ToList();
        _rules = list;
        _compiled = CustomRuleCompiler.Compile(list);
    }

    /// <summary>整表校验（保存前调用）；返回机器可读错误码，null 表示通过。</summary>
    public static string? ValidateAll(IReadOnlyList<CustomRule> rules)
    {
        if (rules.Count > CustomRuleValidation.MaxRules) return "rule_limit_exceeded";
        foreach (var rule in rules)
        {
            var error = CustomRuleValidation.Validate(rule);
            if (error is not null) return error;
        }

        if (rules.Select(rule => rule.Key).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            return "rule_key_duplicate";

        return null;
    }

    /// <summary>整表写入（JSON 序列化 → 设置存储 → 刷新快照）。</summary>
    public async Task SaveAsync(IReadOnlyList<CustomRule> rules, Guid? updatedBy, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(rules, CustomRuleJson.Options);
        await settings.SetAsync(
            new Dictionary<string, string?> { [SettingKey] = json }, updatedBy, ct);
        Refresh();
    }
}
