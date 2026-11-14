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

/// <summary>
/// 原生供应商直通：/v1/native/{providerAlias}/{path}。
/// 保留上游协议原貌，但仍执行认证、授权、约束、限流、预算与审计。
/// </summary>
public static class NativePassthroughEndpoints
{
    private static readonly string[] ForwardHeaders = ["content-type", "accept", "accept-encoding"];

    public static void MapNativePassthrough(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/v1/native/{providerAlias}/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"], HandleAsync);
    }

    private static async Task HandleAsync(
        string providerAlias, string? path, HttpContext http, ApiKeyAuthenticator authenticator,
        ProviderCredentialStore credentials, AuditWriter audit,
        BudgetGuard budgets, KeyRateLimiter rateLimiter, SystemSettingStore runtime,
        SecureHubDbContext db, IHttpClientFactory httpClientFactory,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        var requestId = "req_" + Guid.NewGuid().ToString("N")[..16];
        http.Response.Headers["x-securehub-request-id"] = requestId;

        var (auth, authError) = await authenticator.AuthenticateAsync(BearerToken(http.Request), ApiKeyAuthenticator.RequestAppName(http.Request), ct);
        if (auth is null)
        {
            await audit.AppendAsync(AuditEventTypes.AuthFailed, "virtualKey", "unknown", "unknown",
                authError ?? ReasonCodes.InvalidApiKey, severity: AuditSeverity.Warning, ct: ct);
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        if (!auth.Constraints.AllowsIp(http.Connection.RemoteIpAddress?.ToString()))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        using var rateLease = rateLimiter.Acquire(auth.VirtualKeyId, auth.Constraints);
        if (!rateLease.Allowed)
        {
            await ApiError.Create(429, ReasonCodes.QuotaExceeded, localizer, requestId).ExecuteAsync(http);
            return;
        }

        var budget = await budgets.CheckAsync(auth.TeamId, ct);
        if (!budget.Allowed)
        {
            await ApiError.Create(429, ReasonCodes.QuotaExceeded, localizer, requestId).ExecuteAsync(http);
            return;
        }

        var provider = await db.Providers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Alias == providerAlias && p.Enabled, ct);
        if (provider is null)
        {
            await ApiError.Create(404, ReasonCodes.ModelNotFound, localizer, requestId).ExecuteAsync(http);
            return;
        }

        if (provider.Scope == ProviderScope.User && provider.OwnerUserId != auth.OwnerId)
        {
            await ApiError.Create(404, ReasonCodes.ModelNotFound, localizer, requestId).ExecuteAsync(http);
            return;
        }

        if (!auth.Constraints.AllowsProvider(provider.Alias))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        if (provider.Scope == ProviderScope.Org)
        {
            var entitlements = await db.Entitlements.AsNoTracking().ToListAsync(ct);
            if (!EntitlementPolicy.AllowsProvider(entitlements, provider.Id, auth.TeamId, auth.OwnerId, clock.UtcNow))
            {
                await ApiError.Forbidden(localizer, ReasonCodes.ModelNotEntitled).ExecuteAsync(http);
                return;
            }
        }

        var upstreamKey = await credentials.GetApiKeyAsync(db, provider.Id, provider.OwnerUserId, ct);
        if (string.IsNullOrEmpty(upstreamKey))
        {
            await ApiError.Create(502, ReasonCodes.AllCandidatesFailed, localizer, requestId).ExecuteAsync(http);
            return;
        }

        byte[] body;
        using (var memory = new MemoryStream())
        {
            await http.Request.Body.CopyToAsync(memory, ct);
            body = memory.ToArray();
        }

        if (body.Length > options.Value.Security.RequestBodyLimitBytes)
        {
            await ApiError.Create(413, "request_blocked", localizer, requestId).ExecuteAsync(http);
            return;
        }

        var queryString = http.Request.QueryString.HasValue ? http.Request.QueryString.Value : string.Empty;
        var target = $"{provider.BaseUrl.TrimEnd('/')}/{path?.TrimStart('/')}{queryString}";
        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(http.Request.Method), target);
        if (body.Length > 0 && http.Request.Method is not ("GET" or "HEAD"))
        {
            upstreamRequest.Content = new ByteArrayContent(body);
            var contentType = http.Request.ContentType;
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        foreach (var header in ForwardHeaders)
        {
            if (http.Request.Headers.TryGetValue(header, out var values))
            {
                upstreamRequest.Headers.TryAddWithoutValidation(header, values.ToArray());
            }
        }
        upstreamRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", upstreamKey);

        var client = httpClientFactory.CreateClient("upstream");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            await RecordAsync(db, auth, provider.Id, providerAlias, requestId, body.Length,
                stopwatch.ElapsedMilliseconds, "upstream_unreachable", ct);
            await ApiError.Create(502, ReasonCodes.AllCandidatesFailed, localizer, requestId).ExecuteAsync(http);
            return;
        }

        using (upstreamResponse)
        {
            stopwatch.Stop();
            http.Response.StatusCode = (int)upstreamResponse.StatusCode;
            if (upstreamResponse.Content.Headers.ContentType is { } responseContentType)
            {
                http.Response.ContentType = responseContentType.ToString();
            }

            await using (var stream = await upstreamResponse.Content.ReadAsStreamAsync(ct))
            {
                await stream.CopyToAsync(http.Response.Body, ct);
            }

            var errorCode = upstreamResponse.IsSuccessStatusCode
                ? null
                : upstreamResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? ReasonCodes.UpstreamRateLimited
                    : ReasonCodes.UpstreamTimeout;

            await RecordAsync(db, auth, provider.Id, providerAlias, requestId, body.Length,
                stopwatch.ElapsedMilliseconds, errorCode, ct);

            await audit.AppendAsync(AuditEventTypes.RouteSelected,
                auth.AuditActorType,
                auth.AuditActorId, auth.Label, errorCode ?? "ok",
                action: errorCode is null ? GuardrailAction.Allow : GuardrailAction.Block,
                severity: errorCode is null ? AuditSeverity.Info : AuditSeverity.Warning,
                billingOwner: auth.Billing,
                configure: e =>
                {
                    e.RequestId = requestId;
                    e.AppName = auth.AppName;
                    e.TeamId = auth.TeamId;
                    e.ProviderId = provider.Id;
                    e.ModelAlias = $"native:{providerAlias}";
                    e.Detail = JsonSerializer.Serialize(new
                    {
                        endpoint = "native",
                        method = http.Request.Method,
                        path,
                        status = (int)upstreamResponse.StatusCode
                    });
                }, ct: ct);
        }
    }

    private static async Task RecordAsync(
        SecureHubDbContext db, AuthContext auth, Guid providerId, string providerAlias,
        string requestId, int requestBytes, long durationMs, string? errorCode, CancellationToken ct)
    {
        var estimatedTokens = Math.Max(1, requestBytes / 4);
        db.UsageEvents.Add(new UsageEvent
        {
            OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            AppName = auth.AppName,
            TeamId = auth.TeamId,
            UserId = auth.OwnerType == KeyOwnerType.User ? auth.OwnerId : null,
            VirtualKeyId = auth.VirtualKeyId,
            ProviderId = providerId,
            ModelScope = ModelScope.Org,
            BillingOwner = auth.Billing,
            Model = $"native:{providerAlias}",
            UpstreamModel = null,
            InputTokens = estimatedTokens,
            InputTokensOriginal = estimatedTokens,
            OutputTokens = 0,
            CostUsd = 0m,
            Estimated = true,
            DurationMs = (int)Math.Min(int.MaxValue, durationMs),
            ErrorCode = errorCode,
            RequestId = requestId
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
