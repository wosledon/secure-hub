using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;

namespace SecureHub.Api.Endpoints;

public sealed record UpdateRuntimeSettingsRequest(
    bool? ContentPersist,
    int? ContentRawDays,
    int? ContentRedactedDays,
    int? ContentCompressedDays,
    int? ContentMaxBytes,
    string? GuardrailsOnError,
    string? GuardrailsRedactionMode,
    int? GuardrailsTokenRetentionHours,
    bool? CompressionEnabled,
    bool? PersonalEnabled,
    string[]? PersonalAllowedProviders,
    Guid[]? PersonalBlockedUsers);

/// <summary>运行期配置：写入数据库后立即生效，无需重启。</summary>
public static class RuntimeSettingsEndpoints
{
    public static void MapRuntimeSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/system/runtime-settings");
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
    }

    private static async Task GetAsync(
        HttpContext http, SessionStore sessions, SystemSettingStore store,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.SystemWrite) is null) return;

        await http.Response.WriteAsJsonAsync(new
        {
            values = store.EffectiveValues(),
            overrides = store.EffectiveValues().Keys.ToDictionary(
                key => key,
                key => store.Get(key) is not null)
        }, ct);
    }

    private static async Task UpdateAsync(
        HttpContext http, UpdateRuntimeSettingsRequest request, SessionStore sessions,
        SystemSettingStore store, AuditWriter audit, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.SystemWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        if (request.ContentRawDays is < 1 or > 3650 ||
            request.ContentRedactedDays is < 1 or > 3650 ||
            request.ContentCompressedDays is < 1 or > 3650 ||
            request.ContentMaxBytes is < 1024 or > 16 * 1024 * 1024 ||
            request.GuardrailsTokenRetentionHours is < 1 or > 720)
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (request.GuardrailsOnError is { } onError &&
            onError is not ("block" or "allow"))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (request.GuardrailsRedactionMode is { } mode &&
            mode is not ("mask" or "placeholder" or "tokenize"))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (request.ContentPersist is { } persist) values["content.persist"] = persist.ToString().ToLowerInvariant();
        if (request.ContentRawDays is { } raw) values["content.raw_days"] = raw.ToString();
        if (request.ContentRedactedDays is { } redacted) values["content.redacted_days"] = redacted.ToString();
        if (request.ContentCompressedDays is { } compressed) values["content.compressed_days"] = compressed.ToString();
        if (request.ContentMaxBytes is { } maxBytes) values["content.max_bytes"] = maxBytes.ToString();
        if (request.GuardrailsOnError is { } guardrailsOnError) values["guardrails.on_error"] = guardrailsOnError;
        if (request.GuardrailsRedactionMode is { } redactionMode) values["guardrails.redaction_mode"] = redactionMode;
        if (request.GuardrailsTokenRetentionHours is { } tokenHours) values["guardrails.token_retention_hours"] = tokenHours.ToString();
        if (request.CompressionEnabled is { } compression) values["compression.enabled"] = compression.ToString().ToLowerInvariant();
        if (request.PersonalEnabled is { } personal) values["personal.enabled"] = personal.ToString().ToLowerInvariant();
        if (request.PersonalAllowedProviders is { } allowed)
        {
            values["personal.allowed_providers"] = string.Join(',', allowed
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        if (request.PersonalBlockedUsers is { } blocked)
        {
            values["personal.blocked_users"] = string.Join(',', blocked.Distinct());
        }

        if (values.Count == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        await store.SetAsync(values, session.UserId, ct);

        await audit.AppendAsync(AuditEventTypes.SystemSettingsUpdated, "user", session.UserId.ToString(),
            session.UserName, "ok", severity: AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new { updated = values.Keys }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            ok = true,
            values = store.EffectiveValues()
        }, ct);
    }
}
