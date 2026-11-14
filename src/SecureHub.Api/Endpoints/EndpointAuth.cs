using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Core.Security;

namespace SecureHub.Api.Endpoints;

/// <summary>新端点组的会话/权限校验；与既有管理端点行为一致。</summary>
public static class EndpointAuth
{
    public static SessionEntry? RequireSession(
        HttpContext http, SessionStore sessions, SecureHubOptions options)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null) return session;

        ApiError.Unauthorized(ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return null;
    }

    public static SessionEntry? Require(
        HttpContext http, SessionStore sessions, SecureHubOptions options, Permission permission)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null && Rbac.AllowsAny(session.Roles, permission)) return session;

        var status = session is null ? 401 : 403;
        var code = session is null ? ReasonCodes.InvalidApiKey : ReasonCodes.InsufficientScope;
        ApiError.Create(status, code, ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return null;
    }
}
