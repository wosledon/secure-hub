using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
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

public static class PolicyEndpoints
{
    public static void MapPolicies(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/policies");
        group.MapGet("", ListAsync);
        group.MapGet("/snapshot", (PolicyStore store) => Snapshot(store));
        group.MapPost("", PublishAsync);
        group.MapPost("/{policyKey}/activate", ActivateAsync);
        group.MapPost("/dry-run", DryRunAsync);
        group.MapPost("/import/preview", PreviewYamlAsync);
    }

    private static IResult Snapshot(HttpContext http, PolicyStore store) =>
        Results.Ok(new
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
                any = p.Condition.Any.Select(d => d.Detector),
                all = p.Condition.All.Select(d => d.Detector)
            })
        });

    private static IResult Snapshot(PolicyStore store) =>
        Results.Ok(new
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
                any = p.Condition.Any.Select(d => d.Detector),
                all = p.Condition.All.Select(d => d.Detector)
            })
        });

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
        PolicyStore store, IOptions<SecureHubOptions> options, CancellationToken ct)
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
                .Select(v => new
                {
                    v.Version,
                    v.Priority,
                    v.ScopeJson,
                    v.WhenJson,
                    v.ThenJson,
                    v.OnError,
                    v.RolloutJson,
                    v.ChangeReason,
                    v.CreatedAt
                })
        }), ct);
    }

    private static async Task PublishAsync(
        HttpContext http, PublishPolicyRequest request, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, IClock clock,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);
        var session = sessions.Get(SessionCookie.Read(http))!;

        if (string.IsNullOrWhiteSpace(request.PolicyKey))
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var policy = await db.SecurityPolicies.FirstOrDefaultAsync(p => p.PolicyKey == request.PolicyKey, ct);
        if (policy is null)
        {
            policy = new SecurityPolicy
            {
                PolicyKey = request.PolicyKey,
                Enabled = true,
                CreatedAt = clock.UtcNow
            };
            db.SecurityPolicies.Add(policy);
        }

        var nextVersion = (await db.SecurityPolicyVersions
            .Where(v => v.PolicyId == policy.Id)
            .Select(v => (int?)v.Version)
            .MaxAsync(ct) ?? 0) + 1;

        var scopeJson = JsonSerializer.Serialize(new PolicyScope
        {
            Teams = request.Scope?.Teams ?? [],
            Apps = request.Scope?.Apps ?? [],
            Models = request.Scope?.Models ?? []
        });

        var whenJson = JsonSerializer.Serialize(new PolicyCondition
        {
            Any = (request.When?.Any ?? []).Select(d => new DetectorRequirement
            {
                Detector = d.Detector,
                MinMatches = d.MinMatches
            }).ToArray(),
            All = (request.When?.All ?? []).Select(d => new DetectorRequirement
            {
                Detector = d.Detector,
                MinMatches = d.MinMatches
            }).ToArray()
        });

        var then = request.Then ?? new PolicyThenDto("block", "policy_matched", null);
        var thenJson = JsonSerializer.Serialize(new PolicyThen
        {
            Action = then.Action,
            Reason = then.Reason,
            RuleId = then.RuleId
        });

        if (request.RolloutPercent is < 0 or > 100)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        db.SecurityPolicyVersions.Add(new SecurityPolicyVersion
        {
            PolicyId = policy.Id,
            Version = nextVersion,
            Priority = request.Priority,
            ScopeJson = scopeJson,
            WhenJson = whenJson,
            ThenJson = thenJson,
            OnError = string.Equals(request.OnError, "allow", StringComparison.OrdinalIgnoreCase) ? "allow" : "block",
            RolloutJson = JsonSerializer.Serialize(new PolicyRollout { Percent = request.RolloutPercent ?? 100 }),
            ChangeReason = request.ChangeReason ?? string.Empty,
            CreatedBy = session.UserId,
            CreatedAt = clock.UtcNow
        });

        policy.ActiveVersion = nextVersion;
        await db.SaveChangesAsync(ct);

        var refreshed = await store.RefreshAsync(ct);

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = policy.PolicyKey,
            version = nextVersion,
            snapshot_reloaded = refreshed,
            error = store.LastError
        }, ct);
    }

    private static async Task ActivateAsync(
        HttpContext http, string policyKey, int? version, SessionStore sessions,
        SecureHubDbContext db, PolicyStore store, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        if (!Require(http, sessions, options.Value, Permission.SecurityWrite)) return;
        var localizer = ApiLocalization.For(http, options);

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

        await http.Response.WriteAsJsonAsync(new
        {
            policy_key = policy.PolicyKey,
            active_version = target,
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

        var required = (request.When?.Any ?? []).Select(d => d.Detector).ToArray();
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
