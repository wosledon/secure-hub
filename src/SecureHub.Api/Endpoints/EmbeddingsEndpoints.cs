using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>OpenAI 兼容的 /v1/embeddings 透传：认证、护栏、路由、限流与计量与聊天端点一致。</summary>
public static class EmbeddingsEndpoints
{
    public static void MapEmbeddings(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/embeddings", HandleAsync);

    private static async Task HandleAsync(
        HttpContext http,
        ApiKeyAuthenticator authenticator,
        GuardrailPipeline guardrails,
        RouteResolver router,
        AuditWriter audit,
        BudgetGuard budgets,
        ProviderCredentialStore credentials,
        KeyRateLimiter rateLimiter,
        SecureHubDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<SecureHubOptions> options,
        IClock clock,
        CancellationToken ct)
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
            await audit.AppendAsync(AuditEventTypes.RateLimited, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                ReasonCodes.QuotaExceeded, action: GuardrailAction.Block, severity: AuditSeverity.Warning,
                billingOwner: auth.Billing, configure: e => e.RequestId = requestId, ct: ct);
            await ApiError.Create(429, ReasonCodes.QuotaExceeded, localizer, requestId).ExecuteAsync(http);
            return;
        }

        var budget = await budgets.CheckAsync(auth.TeamId, ct);
        if (!budget.Allowed)
        {
            await audit.AppendAsync(AuditEventTypes.QuotaExceeded, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                ReasonCodes.QuotaExceeded, action: GuardrailAction.Block, severity: AuditSeverity.Warning,
                billingOwner: auth.Billing, configure: e => e.RequestId = requestId, ct: ct);
            await ApiError.Create(429, ReasonCodes.QuotaExceeded, localizer, requestId).ExecuteAsync(http);
            return;
        }

        string bodyText;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
        {
            bodyText = await reader.ReadToEndAsync(ct);
        }

        if (bodyText.Length > options.Value.Security.RequestBodyLimitBytes)
        {
            await ApiError.Create(413, "request_blocked", localizer, requestId).ExecuteAsync(http);
            return;
        }

        JsonNode? root;
        try { root = JsonNode.Parse(bodyText); }
        catch (JsonException)
        {
            await ApiError.Create(400, "invalid_model_id", localizer, requestId).ExecuteAsync(http);
            return;
        }

        var rawModel = root?["model"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            await ApiError.Create(400, "invalid_model_id", localizer, requestId).ExecuteAsync(http);
            return;
        }

        var inputs = CollectInputs(root!["input"]);
        if (inputs.Count == 0)
        {
            await ApiError.Create(400, ReasonCodes.RequestBlocked, localizer, requestId).ExecuteAsync(http);
            return;
        }

        var verdict = await guardrails.RunAsync(inputs, GuardrailDirection.Inbound, ct);
        if (verdict.Action is GuardrailAction.Block or GuardrailAction.Redact)
        {
            await audit.AppendAsync(AuditEventTypes.GuardrailBlocked, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                verdict.Detections.FirstOrDefault()?.DetectorId ?? ReasonCodes.PiiDetected,
                action: GuardrailAction.Block, severity: AuditSeverity.Warning, billingOwner: auth.Billing,
                configure: e =>
                {
                    e.RequestId = requestId;
                    e.ModelAlias = rawModel;
                }, ct: ct);
            await ApiError.Create(400, ReasonCodes.RequestBlocked, localizer, requestId).ExecuteAsync(http);
            return;
        }

        var decision = await router.ResolveAsync(rawModel, auth.OwnerType, auth.OwnerId, auth.TeamId, ct);
        if (!decision.Success)
        {
            await audit.AppendAsync(AuditEventTypes.RouteDenied, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                decision.ErrorCode ?? ReasonCodes.ModelNotFound, action: GuardrailAction.Block,
                severity: AuditSeverity.Warning, billingOwner: auth.Billing,
                configure: e => e.RequestId = requestId, ct: ct);
            await ApiError.Create(404, decision.ErrorCode ?? ReasonCodes.ModelNotFound, localizer, requestId,
                candidates: decision.Candidates).ExecuteAsync(http);
            return;
        }

        var candidate = decision.Candidate!;
        if (!auth.Constraints.AllowsModel(candidate.UpstreamModel, candidate.ProviderAlias))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        var upstreamKey = await credentials.GetApiKeyAsync(db, candidate.ProviderId, null, ct);
        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == candidate.ProviderId, ct);
        if (string.IsNullOrEmpty(upstreamKey) || provider is null)
        {
            await audit.AppendAsync(AuditEventTypes.RouteDenied, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                ReasonCodes.AllCandidatesFailed, severity: AuditSeverity.Critical, ct: ct);
            await ApiError.Create(502, ReasonCodes.AllCandidatesFailed, localizer, requestId).ExecuteAsync(http);
            return;
        }

        root!["model"] = candidate.UpstreamModel;
        var payload = root.ToJsonString();
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{provider.BaseUrl.TrimEnd('/')}/embeddings")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        upstreamRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", upstreamKey);

        var client = httpClientFactory.CreateClient("upstream");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var upstreamResponse = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        stopwatch.Stop();

        http.Response.StatusCode = (int)upstreamResponse.StatusCode;
        var responseBody = await upstreamResponse.Content.ReadAsStringAsync(ct);

        var inputTokens = 0;
        if (upstreamResponse.IsSuccessStatusCode)
        {
            try
            {
                var responseJson = JsonNode.Parse(responseBody);
                inputTokens = responseJson?["usage"]?["prompt_tokens"]?.GetValue<int>()
                              ?? responseJson?["usage"]?["total_tokens"]?.GetValue<int>()
                              ?? inputs.Sum(item => EstimateTokens(item.Text));
            }
            catch (JsonException)
            {
                inputTokens = inputs.Sum(item => EstimateTokens(item.Text));
            }
        }

        var errorCode = upstreamResponse.IsSuccessStatusCode
            ? null
            : upstreamResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? ReasonCodes.UpstreamRateLimited
                : ReasonCodes.UpstreamTimeout;

        var offering = await db.ProviderOfferings.AsNoTracking().FirstOrDefaultAsync(
            o => o.ProviderId == candidate.ProviderId && o.UpstreamModel == candidate.UpstreamModel, ct);
        var cost = offering is null ? 0m : (inputTokens / 1_000_000m) * offering.InputPricePerMillion;

        db.UsageEvents.Add(new UsageEvent
        {
            OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds()),
            AppName = auth.AppName,
            TeamId = auth.TeamId,
            UserId = auth.OwnerType == KeyOwnerType.User ? auth.OwnerId : null,
            VirtualKeyId = auth.VirtualKeyId,
            ProviderId = provider.Id,
            ModelScope = decision.Model.Scope,
            BillingOwner = decision.BillingOwner,
            Model = rawModel,
            UpstreamModel = candidate.UpstreamModel,
            InputTokens = inputTokens,
            InputTokensOriginal = inputTokens,
            OutputTokens = 0,
            CostUsd = cost,
            Estimated = inputTokens == 0,
            DurationMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
            ErrorCode = errorCode,
            RequestId = requestId
        });
        await db.SaveChangesAsync(ct);
        await budgets.AccumulateAsync(auth.TeamId, inputTokens, cost, ct);
        rateLimiter.AddTokens(auth.VirtualKeyId, inputTokens);

        await audit.AppendAsync(AuditEventTypes.RouteSelected,
            auth.AuditActorType,
            auth.AuditActorId, auth.Label, errorCode ?? "ok",
            action: errorCode is null ? GuardrailAction.Allow : GuardrailAction.Block,
            severity: errorCode is null ? AuditSeverity.Info : AuditSeverity.Warning,
            billingOwner: decision.BillingOwner,
            configure: e =>
            {
                e.RequestId = requestId;
                e.AppName = auth.AppName;
                e.TeamId = auth.TeamId;
                e.ModelAlias = rawModel;
                e.ProviderId = provider.Id;
                e.UpstreamModel = candidate.UpstreamModel;
                e.InputTokens = inputTokens;
                e.CostUsd = cost;
                e.Detail = JsonSerializer.Serialize(new { endpoint = "embeddings" });
            }, ct: ct);

        if (!upstreamResponse.IsSuccessStatusCode)
        {
            await ApiError.Create((int)upstreamResponse.StatusCode,
                upstreamResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? ReasonCodes.UpstreamRateLimited
                    : ReasonCodes.UpstreamTimeout,
                localizer, requestId).ExecuteAsync(http);
            return;
        }

        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(responseBody, ct);
    }

    private static List<(string FieldPath, string Text)> CollectInputs(JsonNode? input)
    {
        var items = new List<(string, string)>();
        if (input is JsonValue value && value.TryGetValue<string>(out var single))
        {
            items.Add(("input", single));
        }
        else if (input is JsonArray array)
        {
            var index = 0;
            foreach (var item in array)
            {
                if (item is JsonValue element && element.TryGetValue<string>(out var text))
                {
                    items.Add(($"input[{index}]", text));
                }
                index++;
            }
        }

        return items;
    }

    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
