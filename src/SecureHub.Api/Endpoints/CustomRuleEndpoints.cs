using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Core.Security.Guardrails;

namespace SecureHub.Api.Endpoints;

public sealed record UpsertCustomRuleRequest(
    string? Key,
    string? Pattern,
    string? MatchType,
    bool? IgnoreCase,
    string? Severity,
    string? Direction,
    bool? Enabled,
    string? Sample,
    string? Description);

/// <summary>自定义敏感词规则（PRD §7.5）：整表读写，保存后立即生效。</summary>
public static class CustomRuleEndpoints
{
    public static void MapCustomRules(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/guardrails/rules");
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{key}", UpdateAsync);
        group.MapDelete("/{key}", DeleteAsync);
    }

    private static async Task ListAsync(
        HttpContext http, SessionStore sessions, CustomRuleStore store,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.SecurityRead) is null) return;

        await http.Response.WriteAsJsonAsync(new
        {
            rules = store.Rules,
            limits = new
            {
                maxRules = CustomRuleValidation.MaxRules,
                maxPatternLength = CustomRuleValidation.MaxPatternLength
            }
        }, ct);
    }

    private static async Task CreateAsync(
        HttpContext http, UpsertCustomRuleRequest request, SessionStore sessions,
        CustomRuleStore store, AuditWriter audit, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.SecurityWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        if (store.Rules.Count >= CustomRuleValidation.MaxRules)
        {
            await ApiError.Create(400, "rule_limit_exceeded", localizer).ExecuteAsync(http);
            return;
        }

        var rule = ToRule(request);
        if (CustomRuleValidation.Validate(rule, isDuplicateKey: store.Contains(rule.DetectorId)) is { } error)
        {
            await ApiError.Create(400, error, localizer).ExecuteAsync(http);
            return;
        }

        var rules = store.Rules.Append(rule).ToList();
        await store.SaveAsync(rules, session.UserId, ct);
        await AuditAsync(audit, AuditEventTypes.CustomRuleCreated, session, rule, ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, rules = store.Rules }, ct);
    }

    private static async Task UpdateAsync(
        HttpContext http, string key, UpsertCustomRuleRequest request, SessionStore sessions,
        CustomRuleStore store, AuditWriter audit, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.SecurityWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var detectorId = $"custom.{key}";
        var index = store.Rules.ToList().FindIndex(rule => rule.DetectorId == detectorId);
        if (index < 0)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        // key 不可改（路径为准），其余字段以请求为准并保留原 key。
        var rule = ToRule(request) with { Key = key };
        if (CustomRuleValidation.Validate(rule) is { } error)
        {
            await ApiError.Create(400, error, localizer).ExecuteAsync(http);
            return;
        }

        var rules = store.Rules.ToList();
        rules[index] = rule;
        await store.SaveAsync(rules, session.UserId, ct);
        await AuditAsync(audit, AuditEventTypes.CustomRuleUpdated, session, rule, ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, rules = store.Rules }, ct);
    }

    private static async Task DeleteAsync(
        HttpContext http, string key, SessionStore sessions, CustomRuleStore store,
        AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.SecurityWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var detectorId = $"custom.{key}";
        var rules = store.Rules.Where(rule => rule.DetectorId != detectorId).ToList();
        if (rules.Count == store.Rules.Count)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        await store.SaveAsync(rules, session.UserId, ct);
        await audit.AppendAsync(AuditEventTypes.CustomRuleDeleted, "guardrail_rule", key,
            session.UserName, "ok", severity: AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new { key, detectorId }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, rules = store.Rules }, ct);
    }

    private static CustomRule ToRule(UpsertCustomRuleRequest request) => new()
    {
        Key = request.Key?.Trim().ToLowerInvariant() ?? string.Empty,
        Pattern = request.Pattern?.Trim() ?? string.Empty,
        MatchType = string.IsNullOrWhiteSpace(request.MatchType) ? "keyword" : request.MatchType.Trim().ToLowerInvariant(),
        IgnoreCase = request.IgnoreCase ?? true,
        Severity = ParseSeverity(request.Severity),
        Direction = ParseDirection(request.Direction),
        Enabled = request.Enabled ?? true,
        Sample = request.Sample,
        Description = request.Description
    };

    private static GuardrailSeverity ParseSeverity(string? value) => value?.ToLowerInvariant() switch
    {
        "critical" => GuardrailSeverity.Critical,
        "info" => GuardrailSeverity.Info,
        _ => GuardrailSeverity.Warning
    };

    private static GuardrailDirection ParseDirection(string? value) =>
        string.Equals(value, "outbound", StringComparison.OrdinalIgnoreCase)
            ? GuardrailDirection.Outbound
            : GuardrailDirection.Inbound;

    private static Task AuditAsync(
        AuditWriter audit, string type, SessionEntry session, CustomRule rule, CancellationToken ct) =>
        audit.AppendAsync(type, "guardrail_rule", rule.Key, session.UserName, "ok",
            severity: AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                rule.Key,
                rule.Pattern,
                rule.MatchType,
                rule.Severity,
                rule.Direction,
                rule.Enabled
            }, CustomRuleJson.Options), ct: ct);
}
