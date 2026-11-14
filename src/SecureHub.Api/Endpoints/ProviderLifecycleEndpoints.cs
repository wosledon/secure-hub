using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public sealed record UpdateProviderRequest(string? DisplayName, string? BaseUrl, bool? Enabled);
public sealed record RotateCredentialRequest(string ApiKey);

/// <summary>供应商生命周期：编辑、停用、凭据轮换与连通性测试（企业 + 个人 BYOK）。</summary>
public static class ProviderLifecycleEndpoints
{
    public static void MapProviderLifecycle(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/providers");
        admin.MapGet("/{id:guid}", GetAsync);
        admin.MapPut("/{id:guid}", (Guid id, HttpContext http, UpdateProviderRequest request, SessionStore sessions,
            SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            UpdateAsync(id, http, request, sessions, db, audit, options, clock, ct, adminScope: true));
        admin.MapDelete("/{id:guid}", (Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
            AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            DisableAsync(id, http, sessions, db, audit, options, clock, ct, adminScope: true));
        admin.MapPost("/{id:guid}/credential", (Guid id, HttpContext http, RotateCredentialRequest request, SessionStore sessions,
            SecureHubDbContext db, ProviderCredentialStore credentials, AuditWriter audit,
            IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            RotateAsync(id, http, request, sessions, db, credentials, audit, options, clock, ct, adminScope: true));
        admin.MapPost("/{id:guid}/test", (Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
            ProviderCredentialStore credentials, AuditWriter audit, IHttpClientFactory httpClientFactory,
            IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            TestAsync(id, http, sessions, db, credentials, audit, httpClientFactory, options, clock, ct, adminScope: true));

        var me = app.MapGroup("/api/me/providers");
        me.MapPut("/{id:guid}", (Guid id, HttpContext http, UpdateProviderRequest request, SessionStore sessions,
            SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            UpdateAsync(id, http, request, sessions, db, audit, options, clock, ct, adminScope: false));
        me.MapDelete("/{id:guid}", (Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
            AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            DisableAsync(id, http, sessions, db, audit, options, clock, ct, adminScope: false));
        me.MapPost("/{id:guid}/credential", (Guid id, HttpContext http, RotateCredentialRequest request, SessionStore sessions,
            SecureHubDbContext db, ProviderCredentialStore credentials, AuditWriter audit,
            IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            RotateAsync(id, http, request, sessions, db, credentials, audit, options, clock, ct, adminScope: false));
        me.MapPost("/{id:guid}/test", (Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
            ProviderCredentialStore credentials, AuditWriter audit, IHttpClientFactory httpClientFactory,
            IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct) =>
            TestAsync(id, http, sessions, db, credentials, audit, httpClientFactory, options, clock, ct, adminScope: false));
    }

    private static async Task GetAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var (_, provider, error) = await LoadProviderAsync(id, http, sessions, db, options, ct, adminScope: true, write: false);
        if (error is not null) { await error.ExecuteAsync(http); return; }

        var offerings = await db.ProviderOfferings.AsNoTracking()
            .Where(o => o.ProviderId == provider!.Id)
            .OrderBy(o => o.UpstreamModel)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = provider!.Id,
            provider.Alias,
            provider.ProviderKey,
            provider.BaseUrl,
            provider.DisplayName,
            scope = provider.Scope.ToString().ToLowerInvariant(),
            provider.Enabled,
            provider.HealthStatus,
            provider.LastTestedAt,
            provider.LastTestError,
            offerings = offerings.Select(o => new
            {
                o.UpstreamModel,
                o.CanonicalModelId,
                o.ContextWindow,
                o.MaxOutputTokens,
                supports_tools = o.SupportsTools,
                supports_reasoning = o.SupportsReasoning,
                input_price = o.InputPricePerMillion,
                output_price = o.OutputPricePerMillion,
                o.Enabled
            })
        }, ct);
    }

    private static async Task UpdateAsync(
        Guid id, HttpContext http, UpdateProviderRequest request, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct, bool adminScope)
    {
        var (session, provider, error) = await LoadProviderAsync(id, http, sessions, db, options, ct, adminScope, write: true);
        if (error is not null) { await error.ExecuteAsync(http); return; }

        var localizer = ApiLocalization.For(http, options);
        if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            if (!IsValidEndpoint(request.BaseUrl))
            {
                await ApiError.Create(400, "validation.provider_endpoint_required", localizer).ExecuteAsync(http);
                return;
            }

            provider!.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            provider!.DisplayName = request.DisplayName.Trim();
        }

        if (request.Enabled is { } enabled)
        {
            provider!.Enabled = enabled;
        }

        provider!.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.ProviderUpdated, "user", session!.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                provider = provider.Alias,
                enabled = provider.Enabled,
                base_url = provider.BaseUrl
            }), ct: ct);

        await WriteProviderAsync(http, provider, ct);
    }

    private static async Task DisableAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db, AuditWriter audit,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct, bool adminScope)
    {
        var (session, provider, error) = await LoadProviderAsync(id, http, sessions, db, options, ct, adminScope, write: true);
        if (error is not null) { await error.ExecuteAsync(http); return; }

        provider!.Enabled = false;
        provider.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.ProviderDisabled, "user", session!.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { provider = provider.Alias }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, id = provider.Id, enabled = false }, ct);
    }

    private static async Task RotateAsync(
        Guid id, HttpContext http, RotateCredentialRequest request, SessionStore sessions, SecureHubDbContext db,
        ProviderCredentialStore credentials, AuditWriter audit, IOptions<SecureHubOptions> options,
        IClock clock, CancellationToken ct, bool adminScope)
    {
        var (session, provider, error) = await LoadProviderAsync(id, http, sessions, db, options, ct, adminScope, write: true);
        if (error is not null) { await error.ExecuteAsync(http); return; }

        var localizer = ApiLocalization.For(http, options);
        var apiKey = request.ApiKey?.Trim() ?? string.Empty;
        if (apiKey.Length == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var now = clock.UtcNow;
        var existing = await db.ProviderCredentials
            .Where(c => c.ProviderId == provider!.Id && c.RetiredAt == null)
            .ToListAsync(ct);
        foreach (var credential in existing) credential.RetiredAt = now;

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider!.Id,
            OwnerUserId = provider.OwnerUserId,
            CipherText = credentials.Protect(apiKey),
            KeyHint = apiKey.Length <= 4 ? apiKey : apiKey[^4..],
            CreatedAt = now
        });

        provider.UpdatedAt = now;
        provider.HealthStatus = "unknown";
        provider.LastTestError = null;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.ProviderCredentialRotated, "user", session!.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { provider = provider.Alias }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, id = provider.Id, key_hint = apiKey.Length <= 4 ? apiKey : apiKey[^4..] }, ct);
    }

    private static async Task TestAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db, ProviderCredentialStore credentials,
        AuditWriter audit, IHttpClientFactory httpClientFactory, IOptions<SecureHubOptions> options,
        IClock clock, CancellationToken ct, bool adminScope)
    {
        var (session, provider, error) = await LoadProviderAsync(id, http, sessions, db, options, ct, adminScope, write: true);
        if (error is not null) { await error.ExecuteAsync(http); return; }

        var apiKey = await credentials.GetApiKeyAsync(db, provider!.Id, provider.OwnerUserId, ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            await ApiError.Create(400, "validation.required", ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var client = httpClientFactory.CreateClient("upstream");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var healthy = false;
        string? failure = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(provider.BaseUrl));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await client.SendAsync(request, timeout.Token);
            healthy = response.IsSuccessStatusCode;
            if (!healthy) failure = $"HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            failure = "timeout";
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        provider.HealthStatus = healthy ? "healthy" : "unhealthy";
        provider.LastTestedAt = clock.UtcNow;
        provider.UpdatedAt = clock.UtcNow;
        provider.LastTestError = failure?.Length > 500 ? failure[..500] : failure;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(AuditEventTypes.ProviderTested, "user", session!.UserId.ToString(), session.UserName,
            healthy ? "ok" : "upstream_timeout", severity: healthy ? AuditSeverity.Info : AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                provider = provider.Alias,
                healthy,
                error = provider.LastTestError
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            healthy,
            health_status = provider.HealthStatus,
            tested_at = provider.LastTestedAt,
            error = provider.LastTestError
        }, ct);
    }

    private static async Task<(SessionEntry? Session, Provider? Provider, IResult? Error)> LoadProviderAsync(
        Guid id, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct, bool adminScope, bool write)
    {
        var session = adminScope
            ? EndpointAuth.Require(http, sessions, options.Value, write ? Permission.ProviderWrite : Permission.ProviderRead)
            : EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return (null, null, Results.Empty);

        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (provider is null)
        {
            return (session, null, ApiError.NotFound(ApiLocalization.For(http, options)));
        }

        if (!adminScope && (provider.Scope != ProviderScope.User || provider.OwnerUserId != session.UserId))
        {
            return (session, null, ApiError.NotFound(ApiLocalization.For(http, options)));
        }

        return (session, provider, null);
    }

    private static async Task WriteProviderAsync(HttpContext http, Provider provider, CancellationToken ct) =>
        await http.Response.WriteAsJsonAsync(new
        {
            id = provider.Id,
            provider.Alias,
            provider.ProviderKey,
            provider.BaseUrl,
            provider.DisplayName,
            scope = provider.Scope.ToString().ToLowerInvariant(),
            provider.Enabled,
            provider.HealthStatus,
            provider.LastTestedAt,
            provider.LastTestError
        }, ct);

    private static string BuildModelsUri(string baseUrl)
    {
        var root = baseUrl.TrimEnd('/');
        return root.EndsWith("/models", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/models";
    }

    private static bool IsValidEndpoint(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
