using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureHub.Api.Assistant;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Data;

namespace SecureHub.Api.Tests;

/// <summary>
/// 助手护栏工具（PRD §11.5）：按 RBAC 注入、工具内二次鉴权、双路径落库与审计。
/// 用 EF InMemory + 真实 CustomRuleStore / PolicyStore / AuditWriter，不打桩。
/// </summary>
public sealed class AssistantGuardrailToolTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CustomRuleStore _rules;

    public AssistantGuardrailToolTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(Options.Create(new SecureHubOptions()));

        // 库名必须在 options 动作外固定，否则每个 scope 各持一个空库。
        var dbName = "assistant-toolbox-tests-" + Guid.NewGuid();
        services.AddDbContext<SecureHubDbContext>(o => o.UseInMemoryDatabase(dbName));

        services.AddSingleton<ContentCipher>();
        services.AddSingleton<SystemSettingStore>();
        services.AddSingleton<CustomRuleStore>();
        services.AddSingleton<AuditWriter>();
        services.AddSingleton<PolicyStore>();
        services.AddScoped<ContentStore>();
        services.AddScoped<AssistantToolbox>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<SecureHubDbContext>().Database.EnsureCreated();

        _rules = _provider.GetRequiredService<CustomRuleStore>();
    }

    public void Dispose() => _provider.Dispose();

    private static AssistantToolSession Session(params UserRole[] roles) =>
        new(Guid.NewGuid(), "test-model", roles);

    private (IServiceScope Scope, AssistantToolbox Toolbox) NewToolbox()
    {
        var scope = _provider.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<AssistantToolbox>());
    }

    private static AIFunction Tool(IReadOnlyList<AITool> tools, string name) =>
        Assert.Single(tools.OfType<AIFunction>(), t => t.Name == name);

    private static async Task<string> InvokeAsync(AIFunction fn, IDictionary<string, object?> values)
    {
        var args = new AIFunctionArguments();
        foreach (var (name, value) in values)
        {
            args[name] = value;
        }

        var result = await fn.InvokeAsync(args, CancellationToken.None);

        // AIFunction 返回值按 JSON 反序列化：string 结果落成 JsonElement，需要还原。
        return result switch
        {
            null => throw new Xunit.Sdk.XunitException("工具返回 null"),
            string text => text,
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText(),
            _ => throw new Xunit.Sdk.XunitException($"工具返回了意外类型 {result.GetType()}")
        };
    }

    private static Dictionary<string, object?> GuardrailArgs(string type, string? key = null) =>
        new()
        {
            ["type"] = type,
            ["key"] = key,
            ["pattern"] = null,
            ["matchType"] = null,
            ["severity"] = null,
            ["direction"] = null,
            ["description"] = null,
            ["scopeApps"] = null,
            ["priority"] = null,
            ["action"] = null,
            ["detector"] = null,
            ["minMatches"] = null,
            ["reason"] = null
        };

    private async Task<List<AuditEvent>> AuditAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();
        return await db.AuditEvents.AsNoTracking().OrderByDescending(e => e.Sequence).ToListAsync();
    }

    [Fact]
    public void Build_injects_guardrail_tools_only_for_security_roles()
    {
        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var admin = toolbox.Build(Session(UserRole.SecurityAdmin), default);
            Assert.Contains(admin, t => t.Name == "list_guardrails");
            Assert.Contains(admin, t => t.Name == "create_guardrail");

            // Auditor 有 SecurityRead 无 SecurityWrite：可看不可写。
            var auditor = toolbox.Build(Session(UserRole.Auditor), default);
            Assert.Contains(auditor, t => t.Name == "list_guardrails");
            Assert.DoesNotContain(auditor, t => t.Name == "create_guardrail");

            // Developer 两项权限都没有。
            var developer = toolbox.Build(Session(UserRole.Developer), default);
            Assert.DoesNotContain(developer, t => t.Name == "list_guardrails");
            Assert.DoesNotContain(developer, t => t.Name == "create_guardrail");

            // 未登录（无角色）：一个都不注入。
            var anonymous = toolbox.Build(new AssistantToolSession(Guid.NewGuid(), "test-model"), default);
            Assert.DoesNotContain(anonymous, t => t.Name == "list_guardrails");
            Assert.DoesNotContain(anonymous, t => t.Name == "create_guardrail");
        }
    }

    [Fact]
    public async Task Create_guardrail_rule_persists_and_audits()
    {
        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(Session(UserRole.SecurityAdmin), default), "create_guardrail");

            var args = GuardrailArgs("rule", "internal_token");
            args["pattern"] = "ACME-SECRET";
            args["matchType"] = "keyword";
            args["severity"] = "critical";
            args["direction"] = "outbound";
            args["description"] = "内部令牌";

            var json = await InvokeAsync(fn, args);

            Assert.Contains("\"ok\":true", json, StringComparison.Ordinal);
            Assert.Contains("\"detector_id\":\"custom.internal_token\"", json, StringComparison.Ordinal);

            Assert.Single(_rules.Rules);
            Assert.True(_rules.Contains("custom.internal_token"));

            var events = await AuditAsync();
            Assert.Contains(events, e =>
                e.EventType == AuditEventTypes.CustomRuleCreated &&
                e.Detail is not null && e.Detail.Contains("\"source\":\"assistant\"", StringComparison.Ordinal));
            Assert.Contains(events, e => e.EventType == AuditEventTypes.AssistantToolInvoked);
        }
    }

    [Fact]
    public async Task Create_guardrail_policy_persists_and_audits()
    {
        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(Session(UserRole.SecurityAdmin), default), "create_guardrail");

            var args = GuardrailArgs("policy", "block-internal-docs");
            args["scopeApps"] = "hr-portal";
            args["priority"] = 100;
            args["action"] = "block";
            args["detector"] = "pii.email";
            args["minMatches"] = 1;
            args["reason"] = "助手创建";

            var json = await InvokeAsync(fn, args);

            Assert.Contains("\"ok\":true", json, StringComparison.Ordinal);
            Assert.Contains("\"created\":true", json, StringComparison.Ordinal);

            var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();
            var policy = await db.SecurityPolicies.AsNoTracking()
                .SingleAsync(p => p.PolicyKey == "block-internal-docs");
            Assert.Equal(1, policy.ActiveVersion);

            var versions = await db.SecurityPolicyVersions.AsNoTracking()
                .Where(v => v.PolicyId == policy.Id).ToListAsync();
            Assert.Single(versions);

            var events = await AuditAsync();
            Assert.Contains(events, e =>
                e.EventType == AuditEventTypes.PolicyCreated &&
                e.Detail is not null && e.Detail.Contains("\"source\":\"assistant\"", StringComparison.Ordinal));
        }
    }

    /// <summary>Build 之后角色被回收：工具已在手里，二次鉴权必须拒绝——不能只靠「看不见」。</summary>
    [Fact]
    public async Task Create_guardrail_rechecks_permission_at_invocation_time()
    {
        var roles = new List<UserRole> { UserRole.SecurityAdmin };
        var session = new AssistantToolSession(Guid.NewGuid(), "test-model", roles);

        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(session, default), "create_guardrail");

            roles.Clear();

            var args = GuardrailArgs("rule", "after_revoke");
            args["pattern"] = "ACME-SECRET";

            var json = await InvokeAsync(fn, args);

            Assert.Contains("insufficient_scope", json, StringComparison.Ordinal);
            Assert.Empty(_rules.Rules);
            Assert.DoesNotContain(await AuditAsync(), e => e.EventType == AuditEventTypes.CustomRuleCreated);
        }
    }

    [Fact]
    public async Task Create_guardrail_rejects_invalid_rule_without_persisting()
    {
        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(Session(UserRole.SecurityAdmin), default), "create_guardrail");

            var args = GuardrailArgs("rule", "Bad Key!");
            args["pattern"] = "ACME-SECRET";

            var json = await InvokeAsync(fn, args);

            Assert.Contains("rule_key_invalid", json, StringComparison.Ordinal);
            Assert.Empty(_rules.Rules);
            Assert.DoesNotContain(await AuditAsync(), e => e.EventType == AuditEventTypes.CustomRuleCreated);
        }
    }

    [Fact]
    public async Task Create_guardrail_rejects_unknown_type()
    {
        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(Session(UserRole.SecurityAdmin), default), "create_guardrail");

            var json = await InvokeAsync(fn, GuardrailArgs("banana"));

            Assert.Contains("guardrail_type_invalid", json, StringComparison.Ordinal);
            Assert.Empty(_rules.Rules);
        }
    }

    [Fact]
    public async Task List_guardrails_returns_rules_and_limits()
    {
        await _rules.SaveAsync(
            [new SecureHub.Core.Security.Guardrails.CustomRule { Key = "internal_token", Pattern = "ACME-SECRET" }],
            updatedBy: null);

        var (scope, toolbox) = NewToolbox();
        using (scope)
        {
            var fn = Tool(toolbox.Build(Session(UserRole.Auditor), default), "list_guardrails");

            var json = await InvokeAsync(fn, new Dictionary<string, object?>());

            Assert.Contains("custom.internal_token", json, StringComparison.Ordinal);
            Assert.Contains("\"max_rules\"", json, StringComparison.Ordinal);
        }
    }
}
