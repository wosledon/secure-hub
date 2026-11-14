using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Models;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;
namespace SecureHub.Api.Endpoints;

/// <summary>从 models.dev 目录选择供应商与模型后接入；凭据仍只写入本服务。</summary>
public sealed record CreateProviderFromCatalogRequest(
    string Alias,
    string ProviderKey,
    string ApiKey,
    string[] Models,
    string? BaseUrl = null);

public static class CatalogOnboardingEndpoints
{
    public static void MapCatalogOnboarding(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/catalog/providers", (HttpContext http, SessionStore sessions, SecureHubDbContext db, IOptions<SecureHubOptions> options, CancellationToken ct) =>
            ListCatalogProvidersAsync(http, sessions, db, options, ct, requireCatalogRead: true));
        admin.MapGet("/catalog/providers/{providerKey}/models", ListCatalogProviderModelsAsync);
        admin.MapPost("/providers/from-catalog", CreateEnterpriseProviderAsync);

        var me = app.MapGroup("/api/me");
        me.MapGet("/catalog/providers", (HttpContext http, SessionStore sessions, SecureHubDbContext db, IOptions<SecureHubOptions> options, CancellationToken ct) =>
            ListCatalogProvidersAsync(http, sessions, db, options, ct, requireCatalogRead: false));
        me.MapGet("/catalog/providers/{providerKey}/models", ListCatalogProviderModelsAsync);
        me.MapPost("/providers/from-catalog", CreatePersonalProviderAsync);
    }

    private static async Task ListCatalogProvidersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct, bool requireCatalogRead)
    {
        if (requireCatalogRead)
        {
            if (EndpointAuth.Require(http, sessions, options.Value, Permission.CatalogRead) is null) return;
        }
        else if (EndpointAuth.RequireSession(http, sessions, options.Value) is null) return;

        var providers = await db.CatalogProviders.AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(providers.Select(p => new
        {
            provider_key = p.ProviderKey,
            p.Name,
            api = p.Api,
            p.Doc,
            p.Npm,
            env = ParseEnv(p.EnvJson),
            model_count = p.ModelCount,
            p.SyncedAt
        }), ct);
    }

    private static async Task ListCatalogProviderModelsAsync(
        string providerKey, HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.CatalogRead) is null) return;

        var models = await db.CatalogModels.AsNoTracking()
            .Where(m => m.ProviderKey == providerKey)
            .OrderBy(m => m.ModelId)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(models.Select(m => new
        {
            m.ModelId,
            m.Family,
            supports_tools = m.SupportsTools,
            supports_reasoning = m.SupportsReasoning,
            supports_structured_output = m.SupportsStructuredOutput,
            context_window = m.ContextWindow,
            max_output_tokens = m.MaxOutputTokens,
            input_price = m.InputPricePerMillion,
            output_price = m.OutputPricePerMillion
        }), ct);
    }

    private static async Task CreateEnterpriseProviderAsync(
        HttpContext http, CreateProviderFromCatalogRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials, AuditWriter audit,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.ProviderWrite);
        if (session is null) return;

        await CreateProviderAsync(http, request, db, credentials, audit, options, clock,
            ProviderScope.Org, null, session, ct);
    }

    private static async Task CreatePersonalProviderAsync(
        HttpContext http, CreateProviderFromCatalogRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials, AuditWriter audit,
        SystemSettingStore runtime, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);
        if (!runtime.GetBool("personal.enabled", options.Value.PersonalProviders.Enabled) ||
            runtime.GetGuidList("personal.blocked_users").Contains(session.UserId))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.ByokDisabled).ExecuteAsync(http);
            return;
        }

        var allowedProviders = runtime.GetList("personal.allowed_providers");
        if (allowedProviders.Length == 0)
        {
            allowedProviders = options.Value.PersonalProviders.AllowedProviders;
        }

        if (allowedProviders.Length > 0 &&
            !allowedProviders.Contains(request.ProviderKey, StringComparer.OrdinalIgnoreCase))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.ProviderNotAllowed).ExecuteAsync(http);
            return;
        }

        await CreateProviderAsync(http, request, db, credentials, audit, options, clock,
            ProviderScope.User, session.UserId, session, ct);
    }

    private static async Task CreateProviderAsync(
        HttpContext http, CreateProviderFromCatalogRequest request, SecureHubDbContext db,
        ProviderCredentialStore credentials, AuditWriter audit, IOptions<SecureHubOptions> options,
        IClock clock, ProviderScope scope, Guid? ownerUserId, SessionEntry session, CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        var alias = request.Alias?.Trim() ?? string.Empty;
        var providerKey = request.ProviderKey?.Trim() ?? string.Empty;
        var apiKey = request.ApiKey?.Trim() ?? string.Empty;
        var selectedModels = (request.Models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (alias.Length == 0 || providerKey.Length == 0 || apiKey.Length == 0 || selectedModels.Length == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (alias.Length > 64)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (ModelId.ReservedProviderAliases.Contains(alias, StringComparer.OrdinalIgnoreCase) ||
            await db.Providers.AnyAsync(p => p.Alias == alias, ct))
        {
            await ApiError.Create(409, "provider_not_allowed", localizer).ExecuteAsync(http);
            return;
        }

        var catalogProvider = await db.CatalogProviders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProviderKey == providerKey, ct);
        if (catalogProvider is null)
        {
            await ApiError.Create(400, "validation.catalog_provider_not_found", localizer).ExecuteAsync(http);
            return;
        }

        var catalogModels = await db.CatalogModels.AsNoTracking()
            .Where(m => m.ProviderKey == providerKey && selectedModels.Contains(m.ModelId))
            .ToDictionaryAsync(m => m.ModelId, StringComparer.Ordinal, ct);

        if (catalogModels.Count != selectedModels.Length)
        {
            await ApiError.Create(400, "validation.catalog_model_not_found", localizer,
                candidates: selectedModels.Where(model => !catalogModels.ContainsKey(model))).ExecuteAsync(http);
            return;
        }

        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? catalogProvider.Api : request.BaseUrl.Trim();
        if (!IsValidEndpoint(baseUrl))
        {
            await ApiError.Create(400, "validation.provider_endpoint_required", localizer).ExecuteAsync(http);
            return;
        }

        var now = clock.UtcNow;
        var provider = new Provider
        {
            Alias = alias,
            ProviderKey = providerKey,
            BaseUrl = baseUrl!.TrimEnd('/'),
            DisplayName = catalogProvider.Name,
            Scope = scope,
            OwnerUserId = ownerUserId,
            CreatedAt = now
        };
        db.Providers.Add(provider);

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider.Id,
            OwnerUserId = ownerUserId,
            CipherText = credentials.Protect(apiKey),
            KeyHint = apiKey.Length <= 4 ? apiKey : apiKey[^4..],
            CreatedAt = now
        });

        foreach (var modelId in selectedModels)
        {
            var catalogModel = catalogModels[modelId];
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderId = provider.Id,
                UpstreamModel = catalogModel.ModelId,
                CanonicalModelId = catalogModel.CanonicalModelId,
                GroupKey = catalogModel.ModelId,
                SupportsTools = catalogModel.SupportsTools,
                SupportsReasoning = catalogModel.SupportsReasoning,
                SupportsStructuredOutput = catalogModel.SupportsStructuredOutput,
                ContextWindow = catalogModel.ContextWindow,
                MaxOutputTokens = catalogModel.MaxOutputTokens,
                InputPricePerMillion = catalogModel.InputPricePerMillion,
                OutputPricePerMillion = catalogModel.OutputPricePerMillion,
                Enabled = true
            });
        }

        await db.SaveChangesAsync(ct);

        var detail = JsonSerializer.Serialize(new
        {
            provider = provider.Alias,
            catalogProvider = providerKey,
            models = selectedModels.Length,
            scope = scope.ToString().ToLowerInvariant()
        });

        await audit.AppendAsync(
            scope == ProviderScope.Org ? AuditEventTypes.ProviderCreated : AuditEventTypes.ByokEnabled,
            "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = detail, ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = provider.Id,
            provider.Alias,
            models = selectedModels.Length,
            scope = scope.ToString().ToLowerInvariant()
        }, ct);
    }

    private static bool IsValidEndpoint(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string[] ParseEnv(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<string[]>(json) ?? [];
}
