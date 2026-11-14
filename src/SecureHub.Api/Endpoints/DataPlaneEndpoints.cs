using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Compression;
using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Models;
using SecureHub.Core.Options;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Core.Security.Policies;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public static class DataPlaneEndpoints
{
    public static void MapDataPlane(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", ChatCompletionsAsync);
        app.MapGet("/v1/models", ListModelsAsync);
    }

    private static async Task ChatCompletionsAsync(
        HttpContext http,
        ApiKeyAuthenticator authenticator,
        GuardrailPipeline guardrails,
        RouteResolver router,
        ContentStore content,
        AuditWriter audit,
        PolicyStore policies,
        ProviderCredentialStore credentials,
        SecureHubDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        var requestId = "req_" + Guid.NewGuid().ToString("N")[..16];
        http.Response.Headers["x-securehub-request-id"] = requestId;

        // 1) 认证
        var rawKey = BearerToken(http.Request);
        var (auth, authError) = await authenticator.AuthenticateAsync(rawKey, ct);
        if (auth is null)
        {
            await audit.AppendAsync(
                AuditEventTypes.AuthFailed, "virtualKey", "unknown", "unknown",
                authError ?? ReasonCodes.InvalidApiKey,
                severity: AuditSeverity.Warning, ct: ct);
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        // 2) 读取与校验
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
        try
        {
            root = JsonNode.Parse(bodyText);
        }
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

        var wantsStream = root?["stream"]?.GetValue<bool>() ?? false;

        // 3) 原文落盘（护栏之前，拦截也要可取证）
        await content.SaveAsync(requestId, ContentDirection.Request, ContentVariant.Raw, auth.AppId, bodyText, ct);

        // 4) 入向护栏 + 策略评估（动作取两者较严者）
        var texts = CollectTexts(root!);
        var verdict = await guardrails.RunAsync(texts, GuardrailDirection.Inbound, ct);

        var detectorIds = verdict.Detections
            .Select(d => d.DetectorId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var policyDecision = PolicyEvaluator.Evaluate(policies.Snapshot, new PolicyEvalContext(
            auth.AppId,
            auth.TeamId,
            rawModel,
            detectorIds,
            PolicyStore.Bucket(auth.AppId, auth.VirtualKeyId),
            TimeOnly.FromDateTime(DateTime.UtcNow)));

        var effectiveAction = (GuardrailAction)Math.Max((int)verdict.Action, (int)policyDecision.Action);
        var firstPolicy = policyDecision.Hits.FirstOrDefault();

        if (effectiveAction == GuardrailAction.Block)
        {
            var first = verdict.Detections.FirstOrDefault();
            await audit.AppendAsync(
                AuditEventTypes.GuardrailBlocked, auth.OwnerType == KeyOwnerType.App ? "app" : "user",
                auth.OwnerId.ToString(), auth.Label, ReasonCodes.PiiDetected,
                action: GuardrailAction.Block, severity: AuditSeverity.Critical,
                billingOwner: auth.Billing,
                configure: e =>
                {
                    e.RequestId = requestId;
                    e.AppId = auth.AppId;
                    e.TeamId = auth.TeamId;
                    e.RuleId = first?.RuleId ?? firstPolicy?.RuleId;
                    e.PolicyId = firstPolicy?.PolicyKey;
                    e.PolicyVersion = firstPolicy?.Version;
                    e.ModelAlias = rawModel;
                    e.Detail = JsonSerializer.Serialize(new
                    {
                        hits = verdict.Detections.Select(d => d.RuleId),
                        policies = policyDecision.Hits.Select(h => h.PolicyKey)
                    });
                }, ct: ct);

            await ApiError.Create(400, ReasonCodes.RequestBlocked, localizer, requestId,
                candidates: verdict.Detections.Select(d => d.RuleId)).ExecuteAsync(http);
            return;
        }

        // 5) 脱敏
        if (effectiveAction == GuardrailAction.Redact)
        {
            RedactInPlace(root!, verdict);
            bodyText = root!.ToJsonString();
            await content.SaveAsync(requestId, ContentDirection.Request, ContentVariant.Redacted, auth.AppId, bodyText, ct);
        }
        else
        {
            await content.SaveAsync(requestId, ContentDirection.Request, ContentVariant.Redacted, auth.AppId, bodyText, ct);
        }

        // 6) 压缩
        var compressionSettings = new CompressionSettings(
            options.Value.Compression.Enabled,
            options.Value.Compression.DetectBoilerplate,
            options.Value.Compression.CollapseRepetition,
            options.Value.Compression.MinRepeats,
            options.Value.Compression.MinSavingsRatio);

        var originalTokens = EstimateTokens(bodyText);
        var (compressedTexts, rolledBack) = CompressInPlace(root!, compressionSettings);
        var outboundBody = root!.ToJsonString();
        await content.SaveAsync(requestId, ContentDirection.Request, ContentVariant.Compressed, auth.AppId, outboundBody, ct);

        // 7) 路由
        var decision = await router.ResolveAsync(rawModel, auth.OwnerType, auth.OwnerId, auth.TeamId, ct);
        if (!decision.Success)
        {
            await audit.AppendAsync(
                AuditEventTypes.RouteDenied, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                decision.ErrorCode ?? ReasonCodes.ModelNotFound,
                action: GuardrailAction.Block, severity: AuditSeverity.Warning,
                billingOwner: auth.Billing,
                configure: e =>
                {
                    e.RequestId = requestId;
                    e.ModelAlias = rawModel;
                    e.Detail = JsonSerializer.Serialize(new { candidates = decision.Candidates });
                }, ct: ct);

            var status = decision.ErrorCode == ReasonCodes.ModelAmbiguous ? 400 : 404;
            await ApiError.Create(status, decision.ErrorCode ?? ReasonCodes.ModelNotFound, localizer, requestId,
                candidates: decision.Candidates).ExecuteAsync(http);
            return;
        }

        var candidate = decision.Candidate!;

        // 8) 凭据
        var upstreamKey = await credentials.GetApiKeyAsync(db, candidate.ProviderId, null, ct);
        if (string.IsNullOrEmpty(upstreamKey))
        {
            await audit.AppendAsync(
                AuditEventTypes.RouteDenied, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                "provider_not_allowed",
                severity: AuditSeverity.Critical, ct: ct);
            await ApiError.Create(502, ReasonCodes.AllCandidatesFailed, localizer, requestId).ExecuteAsync(http);
            return;
        }

        // 9) 转发
        var provider = await db.Providers.FindAsync(new object[] { candidate.ProviderId }, ct);
        if (provider is null)
        {
            await ApiError.Create(502, ReasonCodes.AllCandidatesFailed, localizer, requestId).ExecuteAsync(http);
            return;
        }

        root!["model"] = candidate.UpstreamModel;
        var payload = root.ToJsonString();
        var target = $"{provider.BaseUrl.TrimEnd('/')}/chat/completions";

        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        upstreamRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", upstreamKey);

        var client = httpClientFactory.CreateClient("upstream");
        using var upstreamResponse = await client.SendAsync(
            upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        http.Response.StatusCode = (int)upstreamResponse.StatusCode;

        if (!upstreamResponse.IsSuccessStatusCode)
        {
            var errorBody = await upstreamResponse.Content.ReadAsStringAsync(ct);
            await audit.AppendAsync(
                AuditEventTypes.RouteDenied, "virtualKey", auth.OwnerId.ToString(), auth.Label,
                upstreamResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? ReasonCodes.UpstreamRateLimited
                    : ReasonCodes.UpstreamTimeout,
                severity: AuditSeverity.Warning, billingOwner: decision.BillingOwner,
                configure: e =>
                {
                    e.RequestId = requestId;
                    e.ModelAlias = rawModel;
                    e.ProviderId = provider.Id;
                    e.UpstreamModel = candidate.UpstreamModel;
                }, ct: ct);

            // 不透传上游 body，避免泄露凭据或内部信息
            await ApiError.Create(
                (int)upstreamResponse.StatusCode, ReasonCodes.UpstreamTimeout, localizer, requestId).ExecuteAsync(http);
            return;
        }

        if (wantsStream)
        {
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(http.Response.Body, ct);
            await http.Response.Body.FlushAsync(ct);

            await RecordUsageAsync(
                audit, content, auth, decision, provider.Id, candidate.UpstreamModel,
                requestId, rawModel, originalTokens, EstimateTokens(outboundBody),
                0, estimated: true, rolledBack, ct);
            return;
        }

        var responseBody = await upstreamResponse.Content.ReadAsStringAsync(ct);
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(responseBody, ct);

        var (inputTokens, outputTokens) = ReadUsage(responseBody);
        await RecordUsageAsync(
            audit, content, auth, decision, provider.Id, candidate.UpstreamModel,
            requestId, rawModel, originalTokens, inputTokens,
            outputTokens, estimated: inputTokens == 0, rolledBack, ct);
    }

    private static async Task RecordUsageAsync(
        AuditWriter audit,
        ContentStore content,
        AuthContext auth,
        RouteDecision decision,
        Guid providerId,
        string upstreamModel,
        string requestId,
        string requestedModel,
        int originalTokens,
        int inputTokens,
        int outputTokens,
        bool estimated,
        bool compressionRolledBack,
        CancellationToken ct)
    {
        await audit.AppendAsync(
            AuditEventTypes.RouteSelected,
            auth.OwnerType == KeyOwnerType.App ? "app" : "user",
            auth.OwnerId.ToString(),
            auth.Label,
            compressionRolledBack ? ReasonCodes.CompressionRolledBack : "ok",
            billingOwner: decision.BillingOwner,
            configure: e =>
            {
                e.RequestId = requestId;
                e.AppId = auth.AppId;
                e.TeamId = auth.TeamId;
                e.ModelAlias = requestedModel;
                e.ProviderId = providerId;
                e.UpstreamModel = upstreamModel;
                e.InputTokens = inputTokens;
                e.InputTokensOriginal = originalTokens;
                e.OutputTokens = outputTokens;
                e.Detail = JsonSerializer.Serialize(new { estimated, rolledBack = compressionRolledBack });
            }, ct: ct);
    }

    private static async Task ListModelsAsync(
        HttpContext http,
        ApiKeyAuthenticator authenticator,
        SecureHubDbContext db,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        var (auth, _) = await authenticator.AuthenticateAsync(BearerToken(http.Request), ct);
        if (auth is null)
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        var providers = await db.Providers.AsNoTracking().ToListAsync(ct);
        var offerings = await db.ProviderOfferings.AsNoTracking().ToListAsync(ct);

        var items = new List<object>();
        foreach (var offering in offerings.Where(o => o.Enabled))
        {
            var provider = providers.FirstOrDefault(p => p.Id == offering.ProviderId && p.Enabled);
            if (provider is null) continue;

            if (provider.Scope == ProviderScope.User && provider.OwnerUserId != auth.OwnerId) continue;
            if (provider.Scope == ProviderScope.Org && auth.OwnerType == KeyOwnerType.User && !options.Value.PersonalProviders.Enabled)
            {
                // 个人仍可访问企业分配的模型；此处仅作配置兜底，不阻断
            }

            var scope = provider.Scope == ProviderScope.User ? ModelScope.User : ModelScope.Org;
            items.Add(new
            {
                id = $"{ModelId.ScopePrefix(scope)}/{provider.Alias}/{offering.UpstreamModel}",
                @object = "model",
                owned_by = provider.ProviderKey,
                x_securehub = new
                {
                    source = provider.Scope == ProviderScope.User ? "personal" : "enterprise",
                    billing = provider.Scope == ProviderScope.User ? "user" : "org",
                    context_window = offering.ContextWindow
                }
            });
        }

        await http.Response.WriteAsJsonAsync(new { @object = "list", data = items }, ct);
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    private static List<(string Path, string Text)> CollectTexts(JsonNode root)
    {
        var result = new List<(string, string)>();
        if (root["messages"] is not JsonArray messages) return result;

        for (var i = 0; i < messages.Count; i++)
        {
            var content = messages[i]?["content"];
            if (content is JsonValue value && value.TryGetValue<string>(out var text))
            {
                result.Add(($"messages[{i}].content", text));
            }
        }

        return result;
    }

    private static void RedactInPlace(JsonNode root, GuardrailVerdict verdict)
    {
        if (root["messages"] is not JsonArray messages) return;

        for (var i = 0; i < messages.Count; i++)
        {
            var content = messages[i]?["content"];
            if (content is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;

            var relevant = verdict.Detections.Where(d => d.FieldPath == $"messages[{i}].content");
            var (redacted, _) = Redactor.Replace(text, relevant, mask: true);
            messages[i]!["content"] = redacted;
        }
    }

    private static (int Compressed, bool RolledBack) CompressInPlace(JsonNode root, CompressionSettings settings)
    {
        if (root["messages"] is not JsonArray messages) return (0, false);

        var total = 0;
        var rolledBack = false;

        for (var i = 0; i < messages.Count; i++)
        {
            var content = messages[i]?["content"];
            if (content is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;

            var result = CompressionPipeline.Compress(text, settings);
            if (result.RolledBack) rolledBack = true;

            total += result.Text.Length;
            messages[i]!["content"] = result.Text;
        }

        return (total, rolledBack);
    }

    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    private static (int Input, int Output) ReadUsage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var usage = node?["usage"];
            if (usage is null) return (0, 0);

            return (usage["prompt_tokens"]?.GetValue<int>() ?? 0,
                    usage["completion_tokens"]?.GetValue<int>() ?? 0);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }
}
