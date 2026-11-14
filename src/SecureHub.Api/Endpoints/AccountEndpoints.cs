using System.Net.Mail;
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

public sealed record RegisterRequest(
    string UserName,
    string Password,
    string? DisplayName,
    string? Email);

public sealed record UpdateProfileRequest(string? DisplayName, string? Email);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>自助注册、个人资料与密码修改；所有写操作都按当前会话收窄并写入审计。</summary>
public static class AccountEndpoints
{
    private const int MinimumPasswordLength = 12;

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/registration-policy", (IOptions<SecureHubOptions> options) =>
            Results.Ok(new
            {
                enabled = options.Value.Security.AllowSelfRegistration,
                min_password_length = MinimumPasswordLength
            }));

        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapGet("/api/auth/profile", ProfileAsync);
        app.MapPut("/api/auth/profile", UpdateProfileAsync);
        app.MapPost("/api/auth/password", ChangePasswordAsync);
    }

    private static async Task RegisterAsync(
        HttpContext http, RegisterRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IClock clock,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);
        if (!options.Value.Security.AllowSelfRegistration)
        {
            await ApiError.Create(403, "auth.registration_disabled", localizer).ExecuteAsync(http);
            return;
        }

        var userName = request.UserName?.Trim() ?? string.Empty;
        if (userName.Length is 0 or > 100)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinimumPasswordLength)
        {
            await ApiError.Create(400, "validation.password_too_weak", localizer).ExecuteAsync(http);
            return;
        }

        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
        {
            await ApiError.Create(400, "validation.email", localizer).ExecuteAsync(http);
            return;
        }

        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
        {
            await ApiError.Create(409, "validation.duplicate", localizer).ExecuteAsync(http);
            return;
        }

        var roles = new[] { UserRole.EndUser };
        var user = new User
        {
            UserName = userName,
            DisplayName = NormalizeDisplayName(request.DisplayName) ?? userName,
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            RolesJson = JsonSerializer.Serialize(roles),
            IsActive = true,
            CreatedAt = clock.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var token = sessions.Create(user, roles);
        SessionCookie.Write(http, token, options.Value.Security.SessionAbsoluteHours);

        await audit.AppendAsync(
            AuditEventTypes.UserCreated, "user", user.Id.ToString(), user.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                user = user.UserName,
                self_registered = true,
                roles = roles.Select(role => role.ToString())
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            user = new
            {
                id = user.Id,
                user_name = user.UserName,
                display_name = user.DisplayName,
                roles = roles.Select(role => (int)role),
                team_id = user.TeamId
            }
        }, ct);
    }

    private static async Task ProfileAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await ApiError.Create(401, "auth.session_expired", ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var teamName = user.TeamId is { } teamId
            ? await db.Teams.AsNoTracking().Where(t => t.Id == teamId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;

        await http.Response.WriteAsJsonAsync(ProfilePayload(user, teamName, session), ct);
    }

    private static async Task UpdateProfileAsync(
        HttpContext http, UpdateProfileRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);
        var displayName = NormalizeDisplayName(request.DisplayName);
        if (displayName is { Length: > 100 })
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
        {
            await ApiError.Create(400, "validation.email", localizer).ExecuteAsync(http);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await ApiError.Create(401, "auth.session_expired", localizer).ExecuteAsync(http);
            return;
        }

        user.DisplayName = displayName;
        user.Email = email;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.UserUpdated, "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                user = user.UserName,
                self_service = true,
                display_name = user.DisplayName,
                email = user.Email
            }), ct: ct);

        var teamName = user.TeamId is { } teamId
            ? await db.Teams.AsNoTracking().Where(t => t.Id == teamId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;

        await http.Response.WriteAsJsonAsync(ProfilePayload(user, teamName, session), ct);
    }

    private static async Task ChangePasswordAsync(
        HttpContext http, ChangePasswordRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var session = EndpointAuth.RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await ApiError.Create(401, "auth.session_expired", localizer).ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrEmpty(request.CurrentPassword) ||
            !PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            await ApiError.Create(400, "auth.current_password_invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < MinimumPasswordLength)
        {
            await ApiError.Create(400, "validation.password_too_weak", localizer).ExecuteAsync(http);
            return;
        }

        if (PasswordHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            await ApiError.Create(400, "validation.password_same", localizer).ExecuteAsync(http);
            return;
        }

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);

        sessions.RemoveAllForUserExcept(
            user.Id, SessionCookie.Read(http));

        await audit.AppendAsync(
            AuditEventTypes.UserPasswordChanged, "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                user = user.UserName,
                self_service = true
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static object ProfilePayload(User user, string? teamName, SessionEntry session) => new
    {
        id = user.Id,
        user_name = user.UserName,
        display_name = user.DisplayName ?? user.UserName,
        email = user.Email,
        roles = user.Roles.Select(role => (int)role),
        team_id = user.TeamId,
        team_name = teamName,
        created_at = user.CreatedAt,
        totp_enabled = user.TotpSecret?.StartsWith("active:", StringComparison.Ordinal) ?? false,
        session_expires_at = session.ExpiresAt
    };

    private static string? NormalizeDisplayName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string? email) =>
        email is null || MailAddress.TryCreate(email, out _);
}
