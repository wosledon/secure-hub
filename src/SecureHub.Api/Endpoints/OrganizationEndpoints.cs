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

public sealed record CreateTeamRequest(string Name);
public sealed record UpdateTeamRequest(string Name, bool IsActive);
public sealed record CreateUserRequest(string UserName, string Password, Guid? TeamId, int[] Roles);
public sealed record UpdateUserRequest(Guid? TeamId, int[] Roles, bool IsActive);
public sealed record ResetPasswordRequest(string Password);

/// <summary>用户与团队组织管理。仅平台管理员可写；密码只写不读。</summary>
public static class OrganizationEndpoints
{
    private const int MinimumPasswordLength = 12;

    public static void MapOrganization(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/org");
        group.MapGet("/teams", ListTeamsAsync);
        group.MapPost("/teams", CreateTeamAsync);
        group.MapPut("/teams/{id:guid}", UpdateTeamAsync);
        group.MapGet("/users", ListUsersAsync);
        group.MapPost("/users", CreateUserAsync);
        group.MapPut("/users/{id:guid}", UpdateUserAsync);
        group.MapPost("/users/{id:guid}/password", ResetPasswordAsync);
    }

    // ---------- teams ----------

    private static async Task ListTeamsAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite) is null) return;

        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var userCounts = await db.Users.AsNoTracking()
            .Where(u => u.TeamId != null)
            .GroupBy(u => u.TeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(item => item.TeamId, item => item.Count, ct);

        await http.Response.WriteAsJsonAsync(teams.Select(team => new
        {
            team.Id,
            team.Name,
            is_active = team.IsActive,
            team.CreatedAt,
            user_count = userCounts.GetValueOrDefault(team.Id)
        }), ct);
    }

    private static async Task CreateTeamAsync(
        HttpContext http, CreateTeamRequest request, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (await db.Teams.AnyAsync(t => t.Name == name, ct))
        {
            await ApiError.Create(409, "validation.duplicate", localizer).ExecuteAsync(http);
            return;
        }

        var team = new Team { Name = name, CreatedAt = clock.UtcNow };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.TeamCreated, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { team = team.Name }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = team.Id, team.Name, is_active = team.IsActive }, ct);
    }

    private static async Task UpdateTeamAsync(
        Guid id, HttpContext http, UpdateTeamRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (await db.Teams.AnyAsync(t => t.Id != id && t.Name == name, ct))
        {
            await ApiError.Create(409, "validation.duplicate", localizer).ExecuteAsync(http);
            return;
        }

        team.Name = name;
        team.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.TeamUpdated, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { team = team.Name, active = team.IsActive }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = team.Id, team.Name, is_active = team.IsActive }, ct);
    }

    // ---------- users ----------

    private static async Task ListUsersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite) is null) return;

        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        await http.Response.WriteAsJsonAsync(users.Select(user => new
        {
            user.Id,
            user.UserName,
            user.TeamId,
            team_name = user.TeamId is { } teamId ? teams.GetValueOrDefault(teamId) : null,
            roles = user.Roles.Select(role => (int)role),
            is_active = user.IsActive,
            user.CreatedAt,
            totp_enabled = user.TotpSecret?.StartsWith("active:", StringComparison.Ordinal) ?? false
        }), ct);
    }

    private static async Task CreateUserAsync(
        HttpContext http, CreateUserRequest request, SessionStore sessions, SecureHubDbContext db,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var userName = request.UserName?.Trim() ?? string.Empty;
        if (userName.Length is 0 or > 100 || request.Password is null || request.Password.Length < MinimumPasswordLength)
        {
            await ApiError.Create(400, "validation.password_too_weak", localizer).ExecuteAsync(http);
            return;
        }

        var roles = ParseRoles(request.Roles);
        if (roles.Count == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
        {
            await ApiError.Create(409, "validation.duplicate", localizer).ExecuteAsync(http);
            return;
        }

        if (!await TeamExistsAsync(db, request.TeamId, ct))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var user = new User
        {
            UserName = userName,
            PasswordHash = PasswordHasher.Hash(request.Password),
            TeamId = request.TeamId,
            RolesJson = SerializeRoles(roles),
            IsActive = true,
            CreatedAt = clock.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(AuditEventTypes.UserCreated, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                user = user.UserName,
                roles = roles.Select(role => role.ToString())
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = user.Id, user.UserName, team_id = user.TeamId, roles = roles.Select(role => (int)role), is_active = user.IsActive }, ct);
    }

    private static async Task UpdateUserAsync(
        Guid id, HttpContext http, UpdateUserRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        var roles = ParseRoles(request.Roles);
        if (roles.Count == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (!await TeamExistsAsync(db, request.TeamId, ct))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var isSelf = user.Id == session.UserId;
        if (isSelf && (!request.IsActive || !roles.Contains(UserRole.PlatformAdmin)))
        {
            await ApiError.Create(409, "validation.self_lockout", localizer).ExecuteAsync(http);
            return;
        }

        var losesAdmin = user.Roles.Contains(UserRole.PlatformAdmin) &&
                         (!request.IsActive || !roles.Contains(UserRole.PlatformAdmin));
        if (losesAdmin && !await HasOtherActiveAdminAsync(db, user.Id, ct))
        {
            await ApiError.Create(409, "validation.last_admin", localizer).ExecuteAsync(http);
            return;
        }

        user.TeamId = request.TeamId;
        user.RolesJson = SerializeRoles(roles);
        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        if (!user.IsActive)
        {
            sessions.RemoveAllForUser(user.Id);
        }

        await audit.AppendAsync(
            user.IsActive ? AuditEventTypes.UserUpdated : AuditEventTypes.UserDeactivated,
            "user", session.UserId.ToString(), session.UserName, "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                user = user.UserName,
                active = user.IsActive,
                roles = roles.Select(role => role.ToString())
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = user.Id, user.UserName, team_id = user.TeamId, roles = roles.Select(role => (int)role), is_active = user.IsActive }, ct);
    }

    private static async Task ResetPasswordAsync(
        Guid id, HttpContext http, ResetPasswordRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = EndpointAuth.Require(http, sessions, options.Value, Permission.UserWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        if (request.Password is null || request.Password.Length < MinimumPasswordLength)
        {
            await ApiError.Create(400, "validation.password_too_weak", localizer).ExecuteAsync(http);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        user.PasswordHash = PasswordHasher.Hash(request.Password);
        await db.SaveChangesAsync(ct);
        sessions.RemoveAllForUser(user.Id);

        await audit.AppendAsync(AuditEventTypes.UserPasswordReset, "user", session.UserId.ToString(),
            session.UserName, "ok", configure: e => e.Detail = JsonSerializer.Serialize(new { user = user.UserName }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true, sessions_revoked = true }, ct);
    }

    private static async Task<bool> TeamExistsAsync(SecureHubDbContext db, Guid? teamId, CancellationToken ct) =>
        teamId is null || await db.Teams.AnyAsync(t => t.Id == teamId.Value, ct);

    private static async Task<bool> HasOtherActiveAdminAsync(
        SecureHubDbContext db, Guid excludedUserId, CancellationToken ct)
    {
        var activeUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != excludedUserId)
            .Select(u => u.RolesJson)
            .ToListAsync(ct);

        return activeUsers.Any(json => JsonRoleParser.Parse(json).Contains(UserRole.PlatformAdmin));
    }

    private static List<UserRole> ParseRoles(int[]? roles) =>
        (roles ?? [])
            .Where(role => Enum.IsDefined(typeof(UserRole), role))
            .Distinct()
            .Select(role => (UserRole)role)
            .ToList();

    private static string SerializeRoles(IReadOnlyList<UserRole> roles) =>
        JsonSerializer.Serialize(roles.Select(role => role.ToString()));
}
