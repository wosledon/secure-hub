using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Endpoints;
using SecureHub.Api.Services;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;

namespace SecureHub.Api.Assistant;

public sealed record AssistantChatRequest(string Question, string? Model, Guid? SessionId);

public sealed record AssistantSessionRequest(string? Title, string? Model);

public sealed record AssistantChatResponse(
    Guid? SessionId,
    string Answer,
    IReadOnlyList<string> Evidence,
    int ToolCalls,
    string Model,
    string? ToolNames);

public static class AssistantEndpoints
{
    public static void MapAssistant(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/chat", ChatAsync);
        app.MapPost("/api/assistant/chat/stream", ChatStreamAsync);
        app.MapGet("/api/assistant/tools", ListTools);
        app.MapGet("/api/assistant/sessions", ListSessionsAsync);
        app.MapPost("/api/assistant/sessions", CreateSessionAsync);
        app.MapGet("/api/assistant/sessions/{id:guid}", GetSessionAsync);
        app.MapDelete("/api/assistant/sessions/{id:guid}", DeleteSessionAsync);
    }

    private static async Task ChatAsync(
        HttpContext http,
        AssistantChatRequest request,
        SessionStore sessions,
        AuditAssistant assistant,
        AssistantSessionStore sessionStore,
        AuditWriter audit,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        if (!Rbac.AllowsAny(session.Roles, Permission.AssistantUse))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var history = await LoadHistoryAsync(sessionStore, request.SessionId, session.UserId, options, ct);

        var answer = await assistant.AskAsync(request.Question, request.Model, session.UserId, history, ct);

        Guid? sessionId = request.SessionId;
        AssistantAnswer persisted = answer;

        if (sessionId is { } existing)
        {
            var appended = await sessionStore.AppendTurnAsync(existing, session.UserId, request.Question, answer, ct);
            if (!appended) sessionId = null;
        }

        if (sessionId is null)
        {
            // 未带 sessionId：自动开一个会话，方便前端把后续追问串起来。
            var created = await sessionStore.CreateAsync(session.UserId, request.Question, request.Model, ct);
            await sessionStore.AppendTurnAsync(created.Id, session.UserId, request.Question, persisted, ct);
            sessionId = created.Id;

            await audit.AppendAsync(
                AuditEventTypes.AssistantSessionStarted, "user", session.UserId.ToString(), session.UserName,
                "ok", billingOwner: BillingOwner.User,
                configure: e => e.Detail = JsonSerializer.Serialize(new { session_id = created.Id, model = answer.Model }),
                ct: ct);
        }

        await audit.AppendAsync(
            AuditEventTypes.AssistantAnswerReturned, "user", session.UserId.ToString(), session.UserName,
            "ok", billingOwner: BillingOwner.User,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                session_id = sessionId,
                tool_calls = answer.ToolCalls,
                model = answer.Model,
                answer_chars = answer.Answer.Length
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(
            new AssistantChatResponse(sessionId, answer.Answer, answer.Evidence, answer.ToolCalls, answer.Model, answer.ToolNames),
            ct);
    }

    private static async Task ChatStreamAsync(
        HttpContext http,
        AssistantChatRequest request,
        SessionStore sessions,
        AuditAssistant assistant,
        AssistantSessionStore sessionStore,
        AuditWriter audit,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        if (!Rbac.AllowsAny(session.Roles, Permission.AssistantUse))
        {
            await ApiError.Forbidden(localizer, ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var history = await LoadHistoryAsync(sessionStore, request.SessionId, session.UserId, options, ct);

        http.Response.ContentType = "text/event-stream; charset=utf-8";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["x-accel-buffering"] = "no";

        await http.Response.WriteAsync(": connected\n\n", ct);
        await http.Response.Body.FlushAsync(ct);

        var builder = new StringBuilder();
        var toolCalls = 0;
        var model = "none";
        IReadOnlyList<string> evidence = [];
        string? toolNames = null;

        await foreach (var evt in assistant.StreamAsync(request.Question, request.Model, session.UserId, history, ct))
        {
            if (evt.Kind == "delta" && evt.Text is not null)
            {
                builder.Append(evt.Text);
            }
            else if (evt.Kind == "done")
            {
                toolCalls = evt.ToolCalls;
                model = evt.Model ?? model;
                evidence = evt.Evidence ?? [];
                toolNames = evt.ToolNames;
            }

            var payload = JsonSerializer.Serialize(evt, JsonSerializerOptions.Web);
            await http.Response.WriteAsync($"event: {evt.Kind}\ndata: {payload}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }

        var answer = new AssistantAnswer(
            builder.Length > 0 ? builder.ToString() : "模型未返回内容，请换个问法或缩小范围。",
            evidence, toolCalls, model, toolNames);
        Guid? sessionId = request.SessionId;

        if (sessionId is { } existing)
        {
            if (!await sessionStore.AppendTurnAsync(existing, session.UserId, request.Question, answer, ct))
            {
                sessionId = null;
            }
        }

        if (sessionId is null)
        {
            var created = await sessionStore.CreateAsync(session.UserId, request.Question, request.Model, ct);
            await sessionStore.AppendTurnAsync(created.Id, session.UserId, request.Question, answer, ct);
            sessionId = created.Id;

            await audit.AppendAsync(
                AuditEventTypes.AssistantSessionStarted, "user", session.UserId.ToString(), session.UserName,
                "ok", billingOwner: BillingOwner.User,
                configure: e => e.Detail = JsonSerializer.Serialize(new { session_id = created.Id, model }), ct: ct);
        }

        await audit.AppendAsync(
            AuditEventTypes.AssistantAnswerReturned, "user", session.UserId.ToString(), session.UserName,
            "ok", billingOwner: BillingOwner.User,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                session_id = sessionId,
                tool_calls = toolCalls,
                model,
                answer_chars = builder.Length,
                streamed = true
            }), ct: ct);

        // 会话 ID 在流末补发，前端据此把后续追问挂到同一会话。
        var trailer = JsonSerializer.Serialize(
            new { session_id = sessionId, tool_calls = toolCalls, evidence, model },
            JsonSerializerOptions.Web);
        await http.Response.WriteAsync($"event: session\ndata: {trailer}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static async Task ListSessionsAsync(
        HttpContext http,
        SessionStore sessions,
        AssistantSessionStore sessionStore,
        IOptions<SecureHubOptions> options,
        CancellationToken ct,
        int limit = 30)
    {
        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var rows = await sessionStore.ListAsync(session.UserId, limit, ct);

        await http.Response.WriteAsJsonAsync(new
        {
            sessions = rows.Select(s => new
            {
                id = s.Id,
                title = s.Title,
                model = s.ModelAlias,
                messageCount = s.MessageCount,
                createdAt = s.CreatedAt,
                updatedAt = s.UpdatedAt
            })
        }, ct);
    }

    private static async Task CreateSessionAsync(
        HttpContext http,
        AssistantSessionRequest request,
        SessionStore sessions,
        AssistantSessionStore sessionStore,
        AuditWriter audit,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        if (!Rbac.AllowsAny(session.Roles, Permission.AssistantUse))
        {
            await ApiError.Forbidden(ApiLocalization.For(http, options), ReasonCodes.InsufficientScope).ExecuteAsync(http);
            return;
        }

        var created = await sessionStore.CreateAsync(session.UserId, request.Title, request.Model, ct);

        await audit.AppendAsync(
            AuditEventTypes.AssistantSessionStarted, "user", session.UserId.ToString(), session.UserName,
            "ok", billingOwner: BillingOwner.User,
            configure: e => e.Detail = JsonSerializer.Serialize(new { session_id = created.Id, model = request.Model }),
            ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = created.Id,
            title = created.Title,
            model = created.ModelAlias,
            messageCount = created.MessageCount,
            createdAt = created.CreatedAt,
            updatedAt = created.UpdatedAt
        }, ct);
    }

    private static async Task GetSessionAsync(
        HttpContext http,
        Guid id,
        SessionStore sessions,
        AssistantSessionStore sessionStore,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        var entity = await sessionStore.GetAsync(id, session.UserId, ct);
        if (entity is null)
        {
            await ApiError.Create(404, ReasonCodes.SessionNotFound, localizer).ExecuteAsync(http);
            return;
        }

        var messages = await sessionStore.LoadMessagesAsync(id, session.UserId, ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = entity.Id,
            title = entity.Title,
            model = entity.ModelAlias,
            messageCount = entity.MessageCount,
            createdAt = entity.CreatedAt,
            updatedAt = entity.UpdatedAt,
            messages = messages.Select(m => new
            {
                id = m.Id,
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content,
                toolNames = m.ToolNames,
                evidence = string.IsNullOrEmpty(m.EvidenceJson)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(m.EvidenceJson),
                toolCallCount = m.ToolCallCount,
                model = m.Model,
                createdAt = m.CreatedAt
            })
        }, ct);
    }

    private static async Task DeleteSessionAsync(
        HttpContext http,
        Guid id,
        SessionStore sessions,
        AssistantSessionStore sessionStore,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        if (!TryAuthorize(http, sessions, out var session))
        {
            await ApiError.Unauthorized(localizer).ExecuteAsync(http);
            return;
        }

        var deleted = await sessionStore.DeleteAsync(id, session.UserId, ct);
        if (!deleted)
        {
            await ApiError.Create(404, ReasonCodes.SessionNotFound, localizer).ExecuteAsync(http);
            return;
        }

        http.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static IResult ListTools() => Results.Ok(new
    {
        tools = new[]
        {
            new { name = "search_audit_events", readOnly = true },
            new { name = "get_usage_summary", readOnly = true },
            new { name = "get_cost_trend", readOnly = true },
            new { name = "explain_request", readOnly = true },
            new { name = "search_model_catalog", readOnly = true },
            new { name = "list_models", readOnly = true },
            new { name = "get_gateway_health", readOnly = true },
            new { name = "read_content", readOnly = true }
        }
    });

    private static bool TryAuthorize(HttpContext http, SessionStore sessions, out SessionEntry session)
    {
        var resolved = sessions.Get(SessionCookie.Read(http));
        session = resolved!;
        return resolved is not null;
    }

    private static Task<List<(AssistantMessageRole Role, string Content)>> LoadHistoryAsync(
        AssistantSessionStore store,
        Guid? sessionId,
        Guid userId,
        IOptions<SecureHubOptions> options,
        CancellationToken ct) =>
        sessionId is { } id
            ? store.LoadHistoryAsync(id, userId, options.Value.Assistant.MaxHistoryMessages, ct)
            : Task.FromResult(new List<(AssistantMessageRole, string)>());
}
