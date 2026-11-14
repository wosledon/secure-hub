using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Domain;
using SecureHub.Core.Security.Policies;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>
/// 策略快照：启动时编译，变更后 Refresh（PRD §7.3.3 原子替换）。
/// 编译失败时不替换旧快照，并记录错误——绝不带着半套策略运行。
/// </summary>
public sealed class PolicyStore(IServiceScopeFactory scopeFactory, ILogger<PolicyStore> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private volatile CompiledPolicy[] _snapshot = [];
    private volatile string? _lastError;

    public IReadOnlyList<CompiledPolicy> Snapshot => _snapshot;
    public string? LastError => _lastError;
    public int Count => _snapshot.Length;

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();

            var policies = await db.SecurityPolicies.AsNoTracking().Where(p => p.Enabled).ToListAsync(ct);
            var versions = await db.SecurityPolicyVersions.AsNoTracking().ToListAsync(ct);

            var compiled = new List<CompiledPolicy>();

            foreach (var policy in policies)
            {
                if (policy.ActiveVersion is null) continue;

                var version = versions.FirstOrDefault(v =>
                    v.PolicyId == policy.Id && v.Version == policy.ActiveVersion);

                if (version is null)
                {
                    throw new InvalidOperationException(
                        $"Policy '{policy.PolicyKey}' points to missing version {policy.ActiveVersion}.");
                }

                compiled.Add(Compile(policy, version));
            }

            _snapshot = compiled.ToArray();
            _lastError = null;
            logger.LogInformation("Policy snapshot refreshed: {Count} rules", compiled.Count);
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            logger.LogError(ex, "Policy snapshot refresh failed; keeping previous snapshot");
            return false;
        }
    }

    public static CompiledPolicy Compile(SecurityPolicy policy, SecurityPolicyVersion version)
    {
        var scope = Deserialize<PolicyScope>(version.ScopeJson) ?? new PolicyScope();
        var condition = Deserialize<PolicyCondition>(version.WhenJson) ?? new PolicyCondition();
        var then = Deserialize<PolicyThen>(version.ThenJson) ?? new PolicyThen();
        var rollout = Deserialize<PolicyRollout>(version.RolloutJson) ?? new PolicyRollout();

        return new CompiledPolicy(
            policy.PolicyKey,
            version.Version,
            version.Priority,
            policy.Enabled,
            scope,
            condition,
            PolicyActionParser.Parse(then.Action),
            then.Reason,
            then.RuleId,
            Math.Clamp(rollout.Percent, 0, 100),
            version.OnError);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<T>(json, Json);
    }

    /// <summary>灰度分桶：同 key 结果稳定。</summary>
    public static int Bucket(Guid keyId)
    {
        var payload = keyId.ToString();
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload));
        return (BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF) % 100;
    }
}
