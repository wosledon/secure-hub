using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

/// <summary>
/// 最小 MCP 网关：虚拟密钥认证，暴露只读审计/用量/模型工具，按密钥归属收窄数据范围。
/// 传输采用 MCP Streamable HTTP 的 JSON-RPC 请求/响应形态（无服务端推送）。
/// </summary>
public static class McpEndpoints
{
    private const string ProtocolVersion = "2025-06-18";

    public static void MapMcp(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp", HandleAsync);
        app.MapGet("/mcp", () => Results.Json(new
        {
            name = "securehub-mcp",
            protocol = ProtocolVersion,
            transport = "streamable-http",
            tools = new[] { "search_audit_events", "get_usage_summary", "list_models", "get_gateway_health" }
        }));
    }

    private static async Task HandleAsync(
        HttpContext http, ApiKeyAuthenticator authenticator, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        var (auth, _) = await authenticator.AuthenticateAsync(BearerToken(http.Request), ct);
        if (auth is null)
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        if (!auth.Constraints.AllowsIp(http.Connection.RemoteIpAddress?.ToString()))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(http, null, -32700, "Parse error", ct);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                await WriteErrorAsync(http, null, -32600, "Invalid Request", ct);
                return;
            }

            var method = methodElement.GetString()!;
            JsonElement? id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : null;
            var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement : (JsonElement?)null;

            switch (method)
            {
                case "initialize":
                    await WriteResultAsync(http, id, new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "securehub", version = "0.1.0" }
                    }, ct);
                    return;

                case "notifications/initialized":
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;

                case "ping":
                    await WriteResultAsync(http, id, new { }, ct);
                    return;

                case "tools/list":
                    await WriteResultAsync(http, id, new { tools = ToolDefinitions() }, ct);
                    return;

                case "tools/call":
                    await CallToolAsync(http, id, parameters, auth, db, audit, ct);
                    return;

                default:
                    await WriteErrorAsync(http, id, -32601, $"Method not found: {method}", ct);
                    return;
            }
        }
    }

    private static async Task CallToolAsync(
        HttpContext http, JsonElement? id, JsonElement? parameters, AuthContext auth,
        SecureHubDbContext db, AuditWriter audit, CancellationToken ct)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            await WriteErrorAsync(http, id, -32602, "Missing tool name", ct);
            return;
        }

        var tool = nameElement.GetString()!;
        var arguments = payload.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
            ? args
            : default;

        object result;
        switch (tool)
        {
            case "search_audit_events":
                result = await SearchAuditAsync(db, auth, arguments, ct);
                break;
            case "get_usage_summary":
                result = await UsageSummaryAsync(db, auth, arguments, ct);
                break;
            case "list_models":
                result = await ListModelsAsync(db, auth, ct);
                break;
            case "get_gateway_health":
                result = new { status = "ok", scope = auth.OwnerType.ToString().ToLowerInvariant() };
                break;
            default:
                await WriteErrorAsync(http, id, -32602, $"Unknown tool: {tool}", ct);
                return;
        }

        await audit.AppendAsync("assistant_tool_invoked", "virtualKey", auth.OwnerId.ToString(), auth.Label,
            "ok", billingOwner: auth.Billing,
            configure: e => e.Detail = JsonSerializer.Serialize(new { tool, transport = "mcp" }), ct: ct);

        await WriteResultAsync(http, id, new
        {
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(result) } },
            isError = false
        }, ct);
    }

    private static async Task<object> SearchAuditAsync(
        SecureHubDbContext db, AuthContext auth, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 200);
        var query = db.AuditEvents.AsNoTracking().AsQueryable();

        if (auth.AppId is { } appId) query = query.Where(e => e.AppId == appId);
        else query = query.Where(e => e.TeamId == auth.TeamId || e.ActorId == auth.OwnerId.ToString());

        if (ReadString(args, "event_type") is { Length: > 0 } eventType)
        {
            query = query.Where(e => e.EventType == eventType);
        }

        var rows = await query.OrderByDescending(e => e.Sequence).Take(limit).ToListAsync(ct);
        return new
        {
            count = rows.Count,
            events = rows.Select(e => new
            {
                e.Sequence,
                e.OccurredAt,
                e.EventType,
                action = e.Action.ToString().ToLowerInvariant(),
                e.ReasonCode,
                e.ModelAlias,
                e.InputTokens,
                e.OutputTokens,
                e.BillingOwner
            })
        };
    }

    private static async Task<object> UsageSummaryAsync(
        SecureHubDbContext db, AuthContext auth, JsonElement args, CancellationToken ct)
    {
        var days = ReadInt(args, "days", 7, 1, 90);
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        var query = db.UsageEvents.AsNoTracking().Where(e => e.OccurredAt >= from);

        if (auth.AppId is { } appId) query = query.Where(e => e.AppId == appId);
        else query = query.Where(e => e.TeamId == auth.TeamId || e.UserId == auth.OwnerId);

        var rows = await query.ToListAsync(ct);
        return new
        {
            from,
            days,
            requests = rows.Count,
            input_tokens = rows.Sum(e => e.InputTokens),
            output_tokens = rows.Sum(e => e.OutputTokens),
            cost_usd = rows.Sum(e => e.CostUsd),
            by_model = rows.GroupBy(e => e.Model).Select(g => new
            {
                model = g.Key,
                requests = g.Count(),
                input_tokens = g.Sum(e => e.InputTokens),
                output_tokens = g.Sum(e => e.OutputTokens),
                cost_usd = g.Sum(e => e.CostUsd)
            })
        };
    }

    private static async Task<object> ListModelsAsync(
        SecureHubDbContext db, AuthContext auth, CancellationToken ct)
    {
        var providers = await db.Providers.AsNoTracking().Where(p => p.Enabled).ToListAsync(ct);
        var offerings = await db.ProviderOfferings.AsNoTracking().Where(o => o.Enabled).ToListAsync(ct);
        var entitlements = await db.Entitlements.AsNoTracking().ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var items = new List<string>();
        foreach (var offering in offerings)
        {
            var provider = providers.FirstOrDefault(p => p.Id == offering.ProviderId);
            if (provider is null) continue;
            if (provider.Scope == Core.Domain.ProviderScope.User && provider.OwnerUserId != auth.OwnerId) continue;
            if (provider.Scope == Core.Domain.ProviderScope.Org &&
                !EntitlementPolicy.AllowsModel(entitlements, provider.Id, offering.UpstreamModel, auth.TeamId, auth.OwnerId, now))
            {
                continue;
            }
            if (!auth.Constraints.AllowsModel(offering.UpstreamModel, provider.Alias)) continue;

            var scope = provider.Scope == Core.Domain.ProviderScope.User ? "me" : "org";
            items.Add($"{scope}/{provider.Alias}/{offering.UpstreamModel}");
        }

        return new { count = items.Count, models = items };
    }

    private static object[] ToolDefinitions() =>
    [
        Tool("search_audit_events", "Search audit events visible to this key",
            new { type = "object", properties = new
            {
                event_type = new { type = "string" },
                limit = new { type = "integer", minimum = 1, maximum = 200 }
            } }),
        Tool("get_usage_summary", "Token and cost usage for the last N days",
            new { type = "object", properties = new { days = new { type = "integer", minimum = 1, maximum = 90 } } }),
        Tool("list_models", "List model ids available to this key", new { type = "object", properties = new { } }),
        Tool("get_gateway_health", "Gateway health", new { type = "object", properties = new { } })
    ];

    private static object Tool(string name, string description, object inputSchema) =>
        new { name, description, inputSchema };

    private static int ReadInt(JsonElement args, string name, int fallback, int min, int max) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? Math.Clamp(number, min, max)
            : fallback;

    private static string? ReadString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task WriteResultAsync(HttpContext http, JsonElement? id, object result, CancellationToken ct)
    {
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, result }, ct);
    }

    private static async Task WriteErrorAsync(HttpContext http, JsonElement? id, int code, string message, CancellationToken ct)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, ct);
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
