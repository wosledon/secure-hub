using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Core.Security.Policies;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public sealed record PublishPolicyRequest(
    string PolicyKey,
    int Priority,
    PolicyScopeDto? Scope,
    PolicyWhenDto? When,
    PolicyThenDto? Then,
    string? OnError,
    int? RolloutPercent,
    string? ChangeReason);

public sealed record PolicyScopeDto(string[]? Teams, string[]? Apps, string[]? Models);

public sealed record PolicyDetectorDto(string Detector, int MinMatches);

public sealed record PolicyWhenDto(PolicyDetectorDto[]? Any, PolicyDetectorDto[]? All);

public sealed record PolicyThenDto(string Action, string Reason, string? RuleId);

public sealed record PolicyYamlRequest(string Yaml);

public sealed record PolicyEnabledRequest(bool Enabled);

public sealed record PolicyTestRequest(
    PublishPolicyRequest Policy,
    string SampleText,
    string? AppName,
    string? TeamName,
    string? Model);

public static class PolicyEndpoints
{
    private const int MaxSampleLength = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void MapPolicies(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/policies");
        group.MapGet("", ListAsync);
        group.MapGet("/snapshot", Snapshot);
        group.MapGet("/detectors", Detectors);
        group.MapPost("", PublishAsync);
        group.MapPost("/test", TestAsync);
        group.MapPost("/dry-run", DryRunAsync);
        group.MapPost("/import/preview", PreviewYamlAsync);
        group.MapPost("/{policyKey}/activate", ActivateAsync);
        group.MapPut("/{policyKey}/enabled", SetEnabledAsync);
        group.MapDelete("/{policyKey}", DeleteAsync);
    }

    private static IResult Detectors(HttpContext http, SessionStore sessions,
        CustomRuleStore customRules,
        IOptions<SecureHubOptions> options)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityRead)) return Results.Empty;

        // 内置检测器 + 管理员自定义规则，同一张目录供策略条件选择。
        var descriptors = DetectorCatalog.All
            .Concat(customRules.Rules.Select(rule => rule.ToDescriptor()));

        return Results.Ok(descriptors.Select(detector => new
        {
            id = detector.Id,
            category = detector.Category,
            severity = detector.Severity.ToString().ToLowerInvariant(),
            sample = detector.Sample
        }));
    }

    private static IResult Snapshot(HttpContext http, SessionStore sessions,
        PolicyStore store, IOptions<SecureHubOptions> options)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityRead)) return Results.Empty;

        return Results.Ok(new
        {
            count = store.Count,
            error = store.LastError,
            policies = store.Snapshot.Select(p => new
            {
                policy_key = p.PolicyKey,
                version = p.Version,
                priority = p.Priority,
                action = p.Action.ToString().ToLowerInvariant(),
                rollout = p.RolloutPercent,
                apps = p.Scope.Apps,
                teams = p.Scope.Teams,
                models = p.Scope.Models,
                any = p.Condition.Any.Select(d => d.Detector),
                all = p.Condition.All.Select(d => d.Detector),
                min_matches = p.Condition.Any.Concat(p.Condition.All)
                    .ToDictionary(d => d.Detector, d => Math.Max(1, d.MinMatches)),
                on_error = p.OnError
            })
        });
    }

    private static void NormalizeScalars(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var key in obj.Select(property => property.Key).ToList())
            {
                if (TryConvertScalar(obj[key], out var converted))
                {
                    obj[key] = converted;
                }
                else
                {
                    NormalizeScalars(obj[key]);
                }
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (TryConvertScalar(array[index], out var converted))
                {
                    array[index] = converted;
                }
                else
                {
                    NormalizeScalars(array[index]);
                }
            }
        }
    }

    private static bool TryConvertScalar(
        System.Text.Json.Nodes.JsonNode? node,
        out System.Text.Json.Nodes.JsonNode? converted)
    {
        converted = null;
        if (node is not System.Text.Json.Nodes.JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return false;
        }

        if (int.TryParse(text, out var integer))
        {
            converted = System.Text.Json.Nodes.JsonValue.Create(integer);
            return true;
        }

        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            converted = System.Text.Json.Nodes.JsonValue.Create(number);
            return true;
        }

        if (bool.TryParse(text, out var boolean))
        {
            converted = System.Text.Json.Nodes.JsonValue.Create(boolean);
            return true;
        }

        return false;
    }

    private static async Task PreviewYamlAsync(
        HttpContext http, PolicyYamlRequest request, SessionStore sessions,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;

        var localizer = ApiLocalization.For(http, options);
        if (string.IsNullOrWhiteSpace(request.Yaml))
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        try
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithDuplicateKeyChecking()
                .Build();
            var model = deserializer.Deserialize<object>(request.Yaml);
            var json = System.Text.Json.JsonSerializer.Serialize(model);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var normalized = System.Text.Json.Nodes.JsonNode.Parse(json);
            NormalizeScalars(normalized);
            await http.Response.WriteAsJsonAsync(new
            {
                ok = true,
                policies = normalized
            }, ct);
        }
        catch (Exception)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
        }
    }

    private static async Task ListAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityRead)) return;

        var policies = await db.SecurityPolicies.AsNoTracking().OrderBy(p => p.PolicyKey).ToListAsync(ct);
        var versions = await db.SecurityPolicyVersions.AsNoTracking().ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(policies.Select(p => new
        {
            policy_key = p.PolicyKey,
            enabled = p.Enabled,
            active_version = p.ActiveVersion,
            versions = versions
                .Where(v => v.PolicyId == p.Id)
                .OrderByDescending(v => v.Version)
                .Select(ToVersionDto)
        }), ct);
    }

    private static object ToVersionDto(SecurityPolicyVersion version)
    {
        var scope = Deserialize<PolicyScope>(version.ScopeJson) ?? new PolicyScope();
        var condition = Deserialize<PolicyCondition>(version.WhenJson) ?? new PolicyCondition();
        var then = Deserialize<PolicyThen>(version.ThenJson) ?? new PolicyThen();
        var rollout = Deserialize<PolicyRollout>(version.RolloutJson)?.Percent ?? 100;

        return new
        {
            version = version.Version,
            priority = version.Priority,
            scope = new
            {
                teams = scope.Teams,
                apps = scope.Apps,
                models = scope.Models
            },
            when = new
            {
                any = condition.Any.Select(ToRequirementDto),
                all = condition.All.Select(ToRequirementDto)
            },
            then = new
            {
                action = then.Action,
                reason = then.Reason,
                rule_id = then.RuleId
            },
            rollout_percent = Math.Clamp(rollout, 0, 100),
            on_error = version.OnError,
            change_reason = version.ChangeReason,
            created_at = version.CreatedAt,
            // 原始 JSON 保留给高级用户和导入导出，避免控制台猜测结构。
            scopeJson = version.ScopeJson,
            whenJson = version.WhenJson,
            thenJson = version.ThenJson,
            rolloutJson = version.RolloutJson,
            onError = version.OnError,
            createdAt = version.CreatedAt
        };
    }

    private static object ToRequirementDto(DetectorRequirement requirement) => new
    {
        detector = requirement.Detector,
        min_matches = Math.Max(1, requirement.MinMatches)
    };

    private static async Task PublishAsync(
        HttpContext http, PublishPolicyRequest request, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, AuditWriter audit, IClock clock,
        CustomRuleStore customRules,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);
        var session = sessions.Get(SessionCookie.Read(http))!;

        if (!await ValidateDraftAsync(http, request, localizer, customRules)) return;

        var result = await PolicyDraft.PublishAsync(db, store, request, session.UserId, clock, ct);

        await audit.AppendAsync(
            result.Created ? AuditEventTypes.PolicyCreated : AuditEventTypes.PolicyUpdated,
            "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                policy_key = result.PolicyKey,
                version = result.Version,
                action = result.Action,
                priority = request.Priority
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = result.PolicyKey,
            version = result.Version,
            snapshot_reloaded = result.SnapshotReloaded,
            error = result.StoreError
        }, ct);
    }

    private static async Task TestAsync(
        HttpContext http, PolicyTestRequest request, SessionStore sessions,
        CustomRuleStore customRules,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityRead)) return;
        var localizer = ApiLocalization.For(http, options);

        if (request.Policy is null)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (!await ValidateDraftAsync(http, request.Policy, localizer, customRules)) return;

        if (string.IsNullOrWhiteSpace(request.SampleText))
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var sample = request.SampleText.Length > MaxSampleLength
            ? request.SampleText[..MaxSampleLength]
            : request.SampleText;

        // 试跑使用真实检测器，但给足预算，避免交互式测试被线上 8ms 预算干扰。
        var pipeline = new GuardrailPipeline(
            [new PiiDetector(), new SecretDetector(),
             new CustomRuleDetector(customRules.Compiled, GuardrailDirection.Inbound)],
            new GuardrailOptionsLike(2000, 1000, "block"));

        var verdict = await pipeline.RunAsync(
            [("sample", sample)], GuardrailDirection.Inbound, ct);

        var counts = verdict.Detections
            .GroupBy(d => d.DetectorId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.MatchCount), StringComparer.Ordinal);

        var scope = PolicyDraft.BuildScope(request.Policy);
        var condition = PolicyDraft.BuildCondition(request.Policy);
        var then = PolicyDraft.BuildThen(request.Policy);

        var draft = new CompiledPolicy(
            request.Policy.PolicyKey,
            Version: 0,
            request.Policy.Priority,
            Enabled: true,
            scope,
            condition,
            PolicyActionParser.Parse(then.Action),
            then.Reason,
            then.RuleId,
            Math.Clamp(request.Policy.RolloutPercent ?? 100, 0, 100),
            "block");

        var context = new PolicyEvalContext(
            request.AppName,
            null,
            request.TeamName,
            request.Model,
            counts.Keys.ToArray(),
            0,
            TimeOnly.FromDateTime(DateTime.UtcNow),
            counts);

        var decision = PolicyEvaluator.Evaluate([draft], context);
        var effective = (GuardrailAction)Math.Max((int)verdict.Action, (int)decision.Action);

        await http.Response.WriteAsJsonAsync(new
        {
            detections = verdict.Detections.Select(d => new
            {
                detector = d.DetectorId,
                rule_id = d.RuleId,
                match_count = d.MatchCount,
                field_path = d.FieldPath,
                severity = d.Severity.ToString().ToLowerInvariant(),
                sample_hash = d.SampleHash
            }),
            detector_counts = counts,
            guardrail_action = verdict.Action.ToString().ToLowerInvariant(),
            policy_action = decision.Action.ToString().ToLowerInvariant(),
            effective_action = effective.ToString().ToLowerInvariant(),
            scope_matched = PolicyEvaluator.ScopeMatches(scope, context),
            failed_closed = verdict.FailedClosed,
            hits = decision.Hits.Select(h => new
            {
                policy_key = h.PolicyKey,
                action = h.Action.ToString().ToLowerInvariant(),
                reason = h.Reason
            })
        }, ct);
    }

    private static async Task ActivateAsync(
        HttpContext http, string policyKey, int? version, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, AuditWriter audit,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);
        var session = sessions.Get(SessionCookie.Read(http))!;

        var policy = await db.SecurityPolicies.FirstOrDefaultAsync(p => p.PolicyKey == policyKey, ct);
        if (policy is null)
        {
            await ApiError.Create(404, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var target = version ?? policy.ActiveVersion;
        if (target is null ||
            !await db.SecurityPolicyVersions.AnyAsync(v => v.PolicyId == policy.Id && v.Version == target, ct))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        policy.ActiveVersion = target;
        await db.SaveChangesAsync(ct);
        var refreshed = await store.RefreshAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.PolicyActivated,
            "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                policy_key = policy.PolicyKey,
                version = target
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = policy.PolicyKey,
            active_version = target,
            snapshot_reloaded = refreshed
        }, ct);
    }

    private static async Task SetEnabledAsync(
        HttpContext http, string policyKey, PolicyEnabledRequest request, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, AuditWriter audit,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);
        var session = sessions.Get(SessionCookie.Read(http))!;

        var policy = await db.SecurityPolicies.FirstOrDefaultAsync(p => p.PolicyKey == policyKey, ct);
        if (policy is null)
        {
            await ApiError.Create(404, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        policy.Enabled = request.Enabled;
        await db.SaveChangesAsync(ct);
        var refreshed = await store.RefreshAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.PolicyUpdated,
            "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                policy_key = policy.PolicyKey,
                enabled = policy.Enabled
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = policy.PolicyKey,
            enabled = policy.Enabled,
            snapshot_reloaded = refreshed
        }, ct);
    }

    private static async Task DeleteAsync(
        HttpContext http, string policyKey, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, AuditWriter audit,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);
        var session = sessions.Get(SessionCookie.Read(http))!;

        var policy = await db.SecurityPolicies.FirstOrDefaultAsync(p => p.PolicyKey == policyKey, ct);
        if (policy is null)
        {
            await ApiError.Create(404, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var versions = await db.SecurityPolicyVersions
            .Where(v => v.PolicyId == policy.Id)
            .ToListAsync(ct);

        var versionCount = versions.Count;
        db.SecurityPolicyVersions.RemoveRange(versions);
        db.SecurityPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);

        var refreshed = await store.RefreshAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.PolicyDeleted,
            "user", session.UserId.ToString(), session.UserName, "ok",
            severity: AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                policy_key = policyKey,
                versions = versionCount
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = policyKey,
            deleted_versions = versionCount,
            snapshot_reloaded = refreshed
        }, ct);
    }

    /// <summary>
    /// 回放式 dry-run：基于最近审计中记录的 detector 命中重算。
    /// 缺少完整检测明细时置信度有限，响应会显式标注。
    /// </summary>
    private static async Task DryRunAsync(
        HttpContext http, PublishPolicyRequest request, SessionStore sessions,
        SecureHubDbContext db, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityRead)) return;

        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since && e.RuleId != null)
            .OrderByDescending(e => e.Sequence)
            .Take(1000)
            .ToListAsync(ct);

        var required = (request.When?.Any ?? []).Select(d => d.Detector)
            .Concat((request.When?.All ?? []).Select(d => d.Detector))
            .ToArray();

        var affected = events.Count(e => e.RuleId is not null && required.Any(r =>
            e.RuleId.StartsWith(r, StringComparison.OrdinalIgnoreCase)));

        await http.Response.WriteAsJsonAsync(new
        {
            sampled_events = events.Count,
            estimated_affected = affected,
            confidence = "low",
            note = "基于审计中已记录的 detector 命中估算；未落盘的检测明细无法参与回放。"
        }, ct);
    }

    private static async Task<bool> ValidateDraftAsync(
        HttpContext http, PublishPolicyRequest request, ILocalizer localizer,
        CustomRuleStore customRules)
    {
        var error = PolicyDraft.Validate(request, customRules);
        if (error is null) return true;

        await ApiError.Create(400, error, localizer).ExecuteAsync(http);
        return false;
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Require(
        HttpContext http, SessionStore sessions, SecureHubOptions options, Permission permission)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null && Rbac.AllowsAny(session.Roles, permission)) return true;

        var status = session is null ? 401 : 403;
        var code = session is null ? ReasonCodes.InvalidApiKey : ReasonCodes.InsufficientScope;
        ApiError.Create(status, code, ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return false;
    }
}
