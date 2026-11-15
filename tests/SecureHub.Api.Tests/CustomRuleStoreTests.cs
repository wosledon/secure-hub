using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Data;

namespace SecureHub.Api.Tests;

/// <summary>
/// CustomRuleStore 快照语义：保存即生效、非法规则过滤、损坏 JSON 保留旧快照。
/// 用 EF InMemory + 真实 SystemSettingStore（含 DB 往返），不打桩。
/// </summary>
public sealed class CustomRuleStoreTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SystemSettingStore _settings;
    private readonly CustomRuleStore _store;

    public CustomRuleStoreTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(Options.Create(new SecureHubOptions()));
        // 库名必须在 options 动作外固定：AddDbContext 的 options 是 scoped，
        // 每个 scope 若重新生成 GUID 会各持一个空库，跨 scope 读不到数据。
        var dbName = "custom-rule-tests-" + Guid.NewGuid();
        services.AddDbContext<SecureHubDbContext>(o => o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<SecureHubDbContext>().Database.EnsureCreated();

        _settings = new SystemSettingStore(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SecureHubOptions()),
            new SystemClock());
        _store = new CustomRuleStore(_settings, NullLogger<CustomRuleStore>.Instance);
    }

    public void Dispose() => _provider.Dispose();

    private static CustomRule Rule(string key = "internal_token", string pattern = "ACME-SECRET") =>
        new() { Key = key, Pattern = pattern };

    [Fact]
    public async Task Save_refreshes_snapshot_immediately()
    {
        await _store.SaveAsync([Rule()], updatedBy: null);

        Assert.Single(_store.Rules);
        Assert.Single(_store.Compiled);
        Assert.True(_store.Contains("custom.internal_token"));
        Assert.False(_store.Contains("custom.not_saved"));
    }

    [Fact]
    public async Task Save_persists_and_survives_reload()
    {
        await _store.SaveAsync([Rule()], updatedBy: null);

        // 新实例从同一设置存储冷加载：模拟进程重启后的启动加载。
        var reloaded = new CustomRuleStore(_settings, NullLogger<CustomRuleStore>.Instance);
        reloaded.Refresh();

        Assert.Single(reloaded.Rules);
        Assert.Equal("ACME-SECRET", reloaded.Rules[0].Pattern);
    }

    [Fact]
    public async Task Save_round_trips_enums_as_lowercase_strings()
    {
        var rule = Rule() with
        {
            MatchType = "regex",
            Pattern = "AKIA[0-9A-Z]+",
            Severity = GuardrailSeverity.Critical,
            Direction = GuardrailDirection.Outbound
        };

        await _store.SaveAsync([rule], updatedBy: null);

        var stored = _settings.Get(CustomRuleStore.SettingKey);
        Assert.NotNull(stored);
        Assert.Contains("\"severity\":\"critical\"", stored, StringComparison.Ordinal);
        Assert.Contains("\"direction\":\"outbound\"", stored, StringComparison.Ordinal);

        var back = _store.Rules[0];
        Assert.Equal(GuardrailSeverity.Critical, back.Severity);
        Assert.Equal(GuardrailDirection.Outbound, back.Direction);
        Assert.Equal("regex", back.MatchType);
        Assert.Single(_store.Compiled); // regex 可编译，进快照
        Assert.NotNull(_store.Compiled[0].Regex);
    }

    [Fact]
    public async Task Refresh_filters_invalid_rules_but_keeps_valid_ones()
    {
        // 直接写入原始 JSON（绕过 SaveAsync 的调用方校验），模拟历史脏数据。
        var raw = "[" +
            "{\"key\":\"good_rule\",\"pattern\":\"needle\"}," +
            "{\"key\":\"Bad-Key\",\"pattern\":\"x\"}," +
            "{\"key\":\"no_pattern\",\"pattern\":\"\"}" +
            "]";
        await _settings.SetAsync(
            new Dictionary<string, string?> { [CustomRuleStore.SettingKey] = raw }, null);

        _store.Refresh();

        Assert.Single(_store.Rules);
        Assert.Equal("good_rule", _store.Rules[0].Key);
        Assert.Single(_store.Compiled);
    }

    [Fact]
    public async Task Corrupt_json_keeps_previous_snapshot()
    {
        await _store.SaveAsync([Rule()], updatedBy: null);

        await _settings.SetAsync(
            new Dictionary<string, string?> { [CustomRuleStore.SettingKey] = "{not-json" }, null);
        _store.Refresh();

        // 旧快照原样保留，管道检测不中断。
        Assert.Single(_store.Rules);
        Assert.Single(_store.Compiled);
        Assert.True(_store.Contains("custom.internal_token"));
    }

    [Fact]
    public async Task Refresh_with_missing_setting_clears_snapshot()
    {
        await _store.SaveAsync([Rule()], updatedBy: null);

        await _settings.SetAsync(
            new Dictionary<string, string?> { [CustomRuleStore.SettingKey] = null }, null);
        _store.Refresh();

        Assert.Empty(_store.Rules);
        Assert.Empty(_store.Compiled);
    }

    [Fact]
    public async Task Disabled_rule_is_listed_but_not_compiled()
    {
        await _store.SaveAsync([Rule() with { Enabled = false }], updatedBy: null);

        Assert.Single(_store.Rules);
        Assert.Empty(_store.Compiled);
    }

    [Fact]
    public void ValidateAll_reports_first_error_code()
    {
        Assert.Null(CustomRuleStore.ValidateAll([Rule("ok_key", "a")]));

        Assert.Equal("rule_key_duplicate",
            CustomRuleStore.ValidateAll([Rule("dup", "a"), Rule("dup", "b")]));

        Assert.Equal("rule_match_type_invalid",
            CustomRuleStore.ValidateAll([Rule() with { MatchType = "word" }]));
    }

    [Fact]
    public void ValidateAll_enforces_rule_limit()
    {
        var rules = Enumerable.Range(0, CustomRuleValidation.MaxRules + 1)
            .Select(i => Rule($"key_{i}", "p"))
            .ToList();

        Assert.Equal("rule_limit_exceeded", CustomRuleStore.ValidateAll(rules));
        Assert.Null(CustomRuleStore.ValidateAll(rules.Take(CustomRuleValidation.MaxRules).ToList()));
    }
}
