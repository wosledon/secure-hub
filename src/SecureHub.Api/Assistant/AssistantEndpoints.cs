using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Endpoints;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;

namespace SecureHub.Api.Assistant;

public sealed record AssistantChatRequest(string Question, string? Model);

public static class AssistantEndpoints
{
    public static void MapAssistant(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/chat", ChatAsync);
        app.MapGet("/api/assistant/tools", ListTools);
    }

    private static async Task ChatAsync(
        HttpContext http,
        AssistantChatRequest request,
        SessionStore sessions,
        AuditAssistant assistant,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is null)
        {
            await ApiError.Unauthorized(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        if (!Rbac.AllowsAny(session.Roles, Permission.AssistantUse))
        {
            await ApiError.Forbidden(ApiLocalization.For(http, options), ReasonCodes.InsufficientScope)
                .ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await ApiError.Create(400, "validation.required", ApiLocalization.For(http, options))
                .ExecuteAsync(http);
            return;
        }

        var answer = await assistant.AskAsync(request.Question, request.Model, session.UserId, ct);
        await http.Response.WriteAsJsonAsync(answer, ct);
    }

    private static IResult ListTools() => Results.Ok(new
    {
        tools = new[]
        {
            new { name = "search_audit_events", readOnly = true },
            new { name = "get_usage_summary", readOnly = true },
            new { name = "explain_request", readOnly = true },
            new { name = "list_models", readOnly = true }
        }
    });
}
