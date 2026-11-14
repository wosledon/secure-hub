using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Data;

namespace SecureHub.Api.Services;

/// <summary>
/// 运行期配置：数据库持久化 + 进程内快照，写入后立即生效（无需重启）。
/// 快照由启动时加载和每次写入后刷新，读取路径不做数据库查询。
/// </summary>
public sealed class SystemSettingStore(
    IServiceScopeFactory scopeFactory,
    IOptions<SecureHubOptions> options,
    IClock clock)
{
    private volatile Dictionary<string, string> _snapshot = new(StringComparer.Ordinal);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();
        var rows = await db.SystemSettings.AsNoTracking().ToListAsync(ct);
        _snapshot = rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
    }

    public string? Get(string key) =>
        _snapshot.TryGetValue(key, out var value) ? value : null;

    public bool GetBool(string key, bool fallback) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;

    public int GetInt(string key, int fallback, int min, int max) =>
        int.TryParse(Get(key), out var value) ? Math.Clamp(value, min, max) : fallback;

    public string GetString(string key, string fallback) =>
        string.IsNullOrWhiteSpace(Get(key)) ? fallback : Get(key)!;

    public string[] GetList(string key)
    {
        var raw = Get(key);
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public Guid[] GetGuidList(string key) =>
        GetList(key)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

    public async Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        Guid? updatedBy,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();
        var existing = await db.SystemSettings
            .Where(row => values.Keys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, StringComparer.Ordinal, ct);

        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing.TryGetValue(key, out var row)) db.SystemSettings.Remove(row);
                continue;
            }

            if (existing.TryGetValue(key, out var current))
            {
                current.Value = value;
                current.UpdatedAt = clock.UtcNow;
                current.UpdatedBy = updatedBy;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = clock.UtcNow,
                    UpdatedBy = updatedBy
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await RefreshAsync(ct);
    }

    /// <summary>返回合并了 appsettings 默认值的生效值，便于控制台展示。</summary>
    public Dictionary<string, string> EffectiveValues()
    {
        var settings = options.Value;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content.persist"] = GetBool("content.persist", settings.Content.PersistContent).ToString().ToLowerInvariant(),
            ["content.raw_days"] = GetInt("content.raw_days", settings.Content.RawRetentionDays, 1, 3650).ToString(),
            ["content.redacted_days"] = GetInt("content.redacted_days", settings.Content.RedactedRetentionDays, 1, 3650).ToString(),
            ["content.compressed_days"] = GetInt("content.compressed_days", settings.Content.CompressedRetentionDays, 1, 3650).ToString(),
            ["content.max_bytes"] = GetInt("content.max_bytes", settings.Content.MaxContentBytes, 1024, 16 * 1024 * 1024).ToString(),
            ["guardrails.on_error"] = GetString("guardrails.on_error", settings.Guardrails.OnError),
            ["guardrails.redaction_mode"] = GetString("guardrails.redaction_mode", settings.Guardrails.RedactionMode),
            ["guardrails.token_retention_hours"] = GetInt("guardrails.token_retention_hours", settings.Guardrails.TokenRetentionHours, 1, 720).ToString(),
            ["compression.enabled"] = GetBool("compression.enabled", settings.Compression.Enabled).ToString().ToLowerInvariant(),
            ["personal.enabled"] = GetBool("personal.enabled", settings.PersonalProviders.Enabled).ToString().ToLowerInvariant(),
            ["personal.allowed_providers"] = Get("personal.allowed_providers") ?? string.Join(',', settings.PersonalProviders.AllowedProviders),
            ["personal.blocked_users"] = Get("personal.blocked_users") ?? string.Join(',', settings.PersonalProviders.BlockedUsers)
        };
    }
}
