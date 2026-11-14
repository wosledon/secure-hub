using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureHub.Api.Auth;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Models;
using SecureHub.Core.Options;
using SecureHub.Core.Security;
using SecureHub.Data;

namespace SecureHub.Api.Endpoints;

public sealed record LoginRequest(string UserName, string Password, string? TotpCode = null);
public sealed record CreateKeyRequest(
    string DisplayName,
    KeyOwnerType OwnerType,
    Guid OwnerId,
    string[]? Scopes,
    string? AppName = null,
    Guid? TeamId = null);
public sealed record CreateProviderRequest(
    string Alias,
    string ProviderKey,
    string BaseUrl,
    string ApiKey,
    string[] Models);

public sealed record UpsertBudgetRequest(
    BudgetSubject Subject,
    Guid? SubjectId,
    string Period,
    long? TokenLimit,
    decimal? CostLimit,
    bool Enabled);

public sealed record ModelGroupMemberDto(
    Guid ProviderId,
    string UpstreamModel,
    int Priority,
    int Weight,
    bool Enabled);

public sealed record UpsertModelGroupRequest(
    string Name,
    bool AllowCrossScope,
    string Strategy,
    ModelGroupMemberDto[] Members);

public sealed record CreateDisputeRequest(string DisputeType, string? Note);
public sealed record ActivateTotpRequest(string Code);

public static class ManagementEndpoints
{
    public static void MapManagement(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapPost("/api/auth/logout", Logout);
        app.MapGet("/api/auth/me", Me);
        app.MapPost("/api/auth/totp/enable", EnableTotpAsync);
        app.MapPost("/api/auth/totp/activate", ActivateTotpAsync);

        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/providers", ListProvidersAsync);
        admin.MapPost("/providers", CreateProviderAsync);
        admin.MapGet("/keys", ListKeysAsync);
        admin.MapPost("/keys", CreateKeyAsync);
        admin.MapPost("/keys/{id:guid}/revoke", RevokeKeyAsync);
        admin.MapGet("/audit", QueryAuditAsync);
        admin.MapGet("/audit/verify", VerifyAuditAsync);
        admin.MapGet("/audit/export", ExportAuditAsync);
        admin.MapPost("/audit/{eventId:guid}/dispute", CreateDisputeAsync);
        admin.MapGet("/audit/disputes", ListDisputesAsync);
        admin.MapGet("/usage/summary", UsageSummaryAsync);
        admin.MapGet("/budgets", ListBudgetsAsync);
        admin.MapPost("/budgets", UpsertBudgetAsync);
        admin.MapGet("/model-groups", ListModelGroupsAsync);
        admin.MapPost("/model-groups", UpsertModelGroupAsync);
        admin.MapGet("/catalog", QueryCatalogAsync);
        admin.MapPost("/catalog/sync", SyncCatalogAsync);

        var me = app.MapGroup("/api/me");
        me.MapGet("/providers", ListMyProvidersAsync);
        me.MapPost("/providers", CreateMyProviderAsync);
        me.MapGet("/keys", ListMyKeysAsync);
        me.MapPost("/keys", CreateMyKeyAsync);
        me.MapGet("/usage", MyUsageAsync);
    }

    // ---------- auth ----------

    private static async Task LoginAsync(
        HttpContext http,
        LoginRequest request,
        SecureHubDbContext db,
        SessionStore sessions,
        ProviderCredentialStore credentials,
        IOptions<SecureHubOptions> options,
        CancellationToken ct)
    {
        var localizer = ApiLocalization.For(http, options);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == request.UserName, ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await ApiError.Create(401, "auth.login_failed", localizer).ExecuteAsync(http);
            return;
        }

        // 已启用的 TOTP 必须校验；pending 状态不参与登录校验。
        if (user.TotpSecret is { } stored && stored.StartsWith("active:", StringComparison.Ordinal))
        {
            var secret = credentials.Unprotect(Convert.FromBase64String(stored["active:".Length..]));
            if (!Totp.Verify(secret, request.TotpCode))
            {
                await ApiError.Create(401, "auth.login_failed", localizer).ExecuteAsync(http);
                return;
            }
        }

        var roles = user.Roles;
        var token = sessions.Create(user, roles);
        SessionCookie.Write(http, token, options.Value.Security.SessionAbsoluteHours);

        await http.Response.WriteAsJsonAsync(new
        {
            user = new { id = user.Id, user_name = user.UserName, roles, team_id = user.TeamId }
        }, ct);
    }

    private static IResult Logout(HttpContext http, SessionStore sessions)
    {
        sessions.Remove(SessionCookie.Read(http));
        SessionCookie.Clear(http);
        return Results.Ok(new { ok = true });
    }

    private static IResult Me(HttpContext http, SessionStore sessions)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        return session is null
            ? Results.Unauthorized()
            : Results.Ok(new
            {
                user = new
                {
                    id = session.UserId,
                    user_name = session.UserName,
                    roles = session.Roles,
                    team_id = session.TeamId
                }
            });
    }

    // ---------- admin ----------

    private static async Task ListProvidersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.ProviderRead);
        if (session is null) return;

        var providers = await db.Providers.AsNoTracking().OrderBy(p => p.Alias).ToListAsync(ct);
        await http.Response.WriteAsJsonAsync(providers.Select(p => new
        {
            p.Id,
            p.Alias,
            p.ProviderKey,
            p.BaseUrl,
            p.DisplayName,
            scope = p.Scope.ToString().ToLowerInvariant(),
            p.Enabled,
            p.HealthStatus
        }), ct);
    }

    private static async Task CreateProviderAsync(
        HttpContext http, CreateProviderRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials,
        AuditWriter audit, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.ProviderWrite);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);

        if (ModelIdReserved(request.Alias) || await db.Providers.AnyAsync(p => p.Alias == request.Alias, ct))
        {
            await ApiError.Create(400, "provider_not_allowed", localizer).ExecuteAsync(http);
            return;
        }

        var provider = new Provider
        {
            Alias = request.Alias,
            ProviderKey = request.ProviderKey,
            BaseUrl = request.BaseUrl,
            DisplayName = request.Alias,
            Scope = ProviderScope.Org,
            CreatedAt = clock.UtcNow
        };
        db.Providers.Add(provider);

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider.Id,
            CipherText = credentials.Protect(request.ApiKey),
            KeyHint = Tail(request.ApiKey),
            CreatedAt = clock.UtcNow
        });

        foreach (var model in request.Models.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderId = provider.Id,
                UpstreamModel = model,
                GroupKey = model,
                ContextWindow = 128_000,
                MaxOutputTokens = 4_096
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(
            AuditEventTypes.ProviderCreated, "user", session.UserId.ToString(), session.UserName,
            "ok", ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = provider.Id, provider.Alias }, ct);
    }

    private static async Task ListKeysAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyRead);
        if (session is null) return;

        var keys = await db.VirtualKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(keys.Select(k => new
        {
            id = k.Id,
            key_id = k.KeyId,
            prefix = k.Prefix,
            display_name = k.DisplayName,
            owner_type = k.OwnerType.ToString().ToLowerInvariant(),
            owner_id = k.OwnerId,
            app_name = k.AppName,
            team_id = k.TeamId,
            is_revoked = k.IsRevoked,
            created_at = k.CreatedAt,
            expires_at = k.ExpiresAt,
            constraints = k.ConstraintsJson
        }), ct);
    }

    private static async Task CreateKeyAsync(
        HttpContext http, CreateKeyRequest request, SessionStore sessions,
        SecureHubDbContext db, AuditWriter audit, IOptions<SecureHubOptions> options,
        IClock clock, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyWrite);
        if (session is null) return;
        var localizer = ApiLocalization.For(http, options);

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is 0 or > 200)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        if (!Enum.IsDefined(request.OwnerType))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var appName = NormalizeAppName(request.AppName);
        Guid ownerId;
        Guid? teamId;

        if (request.OwnerType == KeyOwnerType.Organization)
        {
            ownerId = Guid.Empty;
            teamId = request.TeamId;
            if (teamId is { } requestedTeam && !await db.Teams.AnyAsync(t => t.Id == requestedTeam, ct))
            {
                await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
                return;
            }
        }
        else
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.OwnerId, ct);
            if (user is null || !user.IsActive)
            {
                await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
                return;
            }

            ownerId = user.Id;
            teamId = user.TeamId;
        }

        var (fullKey, parsed) = VirtualKeyFormat.Generate();
        var entity = new VirtualKey
        {
            KeyId = parsed.KeyId,
            SecretHash = VirtualKeyFormat.HashSecret(parsed.Secret),
            Prefix = fullKey[..Math.Min(20, fullKey.Length)],
            DisplayName = displayName,
            OwnerType = request.OwnerType,
            OwnerId = ownerId,
            AppName = appName,
            TeamId = teamId,
            ScopesJson = System.Text.Json.JsonSerializer.Serialize(request.Scopes ?? ["chat:write", "models:read"]),
            CreatedAt = clock.UtcNow
        };

        db.VirtualKeys.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.KeyCreated, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = System.Text.Json.JsonSerializer.Serialize(new
            {
                key_id = entity.KeyId,
                owner_type = entity.OwnerType.ToString().ToLowerInvariant(),
                app_name = appName,
                team_id = entity.TeamId
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new
        {
            id = entity.Id,
            key = fullKey,
            key_id = entity.KeyId,
            display_name = entity.DisplayName,
            app_name = entity.AppName
        }, ct);
    }

    private static async Task RevokeKeyAsync(
        HttpContext http, Guid id, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, IClock clock, AuditWriter audit, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.KeyWrite);
        if (session is null) return;

        var key = await db.VirtualKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        key.IsRevoked = true;
        key.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.KeyRevoked, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = $"{{\"keyId\":\"{key.KeyId}\"}}", ct: ct);

        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static async Task QueryAuditAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.AuditRead);
        if (session is null) return;

        var from = ParseDate(http.Request.Query["from"]) ?? DateTimeOffset.UtcNow.AddDays(-7);
        var to = ParseDate(http.Request.Query["to"]) ?? DateTimeOffset.UtcNow;

        var query = db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        if (!string.IsNullOrWhiteSpace(http.Request.Query["app"]))
        {
            var app = http.Request.Query["app"].ToString().Trim();
            query = query.Where(e =>
                (e.AppName != null && e.AppName.Contains(app)) ||
                (e.ActorType == "app" && e.ActorLabel.Contains(app)));
        }
        else if (Guid.TryParse(http.Request.Query["appId"], out var appId))
        {
            query = query.Where(e => e.AppId == appId);
        }

        var eventType = http.Request.Query["eventType"].ToString();
        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        var rows = await query
            .OrderByDescending(e => e.Sequence)
            .Take(200)
            .ToListAsync(ct);

        var items = rows.Select(e => new
        {
            e.EventId,
            e.Sequence,
            e.OccurredAt,
            e.EventType,
            severity = e.Severity.ToString().ToLowerInvariant(),
            action = e.Action.ToString().ToLowerInvariant(),
            e.ActorType,
            e.ActorLabel,
            app_name = e.AppName ?? (e.ActorType == "app" ? e.ActorLabel : null),
            e.RequestId,
            e.ModelAlias,
            e.ProviderId,
            e.UpstreamModel,
            policy_id = e.PolicyId,
            policy_version = e.PolicyVersion,
            e.RuleId,
            e.ReasonCode,
            detail = e.Detail,
            e.InputTokens,
            e.InputTokensOriginal,
            e.OutputTokens,
            billing_owner = e.BillingOwner.ToString().ToLowerInvariant(),
            e.ContentMissing,
            e.Hash
        })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, to, count = items.Count, data = items }, ct);
    }

    private static async Task VerifyAuditAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.AuditRead);
        if (session is null) return;

        var events = await db.AuditEvents.AsNoTracking()
            .OrderBy(e => e.Sequence)
            .Take(5_000)
            .ToListAsync(ct);

        var breakAt = AuditWriter.Verify(events);

        await http.Response.WriteAsJsonAsync(new
        {
            ok = breakAt is null,
            checked_count = events.Count,
            first_break_sequence = breakAt
        }, ct);
    }

    private static async Task UsageSummaryAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireAdmin(http, sessions, options.Value, Permission.UsageRead);
        if (session is null) return;

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from)
            .ToListAsync(ct);

        var grouped = rows
            .GroupBy(e => new { e.Model, e.BillingOwner })
            .Select(g => new
            {
                model = g.Key.Model,
                billing_owner = g.Key.BillingOwner.ToString().ToLowerInvariant(),
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                output_tokens = g.Sum(x => x.OutputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, data = grouped }, ct);
    }

    private static async Task ListBudgetsAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.BudgetWrite) is null) return;

        var rows = await db.Budgets.AsNoTracking()
            .Where(b => b.Subject != BudgetSubject.App)
            .ToListAsync(ct);
        await http.Response.WriteAsJsonAsync(rows.Select(b => new
        {
            b.Id,
            subject = b.Subject.ToString().ToLowerInvariant(),
            b.SubjectId,
            b.Period,
            b.TokenLimit,
            b.CostLimit,
            b.TokensUsed,
            b.CostUsed,
            b.PeriodStart,
            b.Enabled
        }), ct);
    }

    private static async Task UpsertBudgetAsync(
        HttpContext http, UpsertBudgetRequest request, SessionStore sessions,
        SecureHubDbContext db, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.BudgetWrite) is null) return;
        var localizer = ApiLocalization.For(http, options);

        var period = (request.Period ?? "month").ToLowerInvariant();
        if (period is not ("day" or "week" or "month"))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        if (request.Subject is not (BudgetSubject.All or BudgetSubject.Team) ||
            (request.Subject == BudgetSubject.Team && request.SubjectId is null))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var subjectId = request.Subject == BudgetSubject.All ? null : request.SubjectId;

        var budget = await db.Budgets.FirstOrDefaultAsync(b =>
            b.Subject == request.Subject && b.SubjectId == subjectId && b.Period == period, ct);

        if (budget is null)
        {
            budget = new Budget
            {
                Subject = request.Subject,
                SubjectId = subjectId,
                Period = period,
                PeriodStart = clock.UtcNow
            };
            db.Budgets.Add(budget);
        }

        budget.TokenLimit = request.TokenLimit;
        budget.CostLimit = request.CostLimit;
        budget.Enabled = request.Enabled;

        await db.SaveChangesAsync(ct);
        await http.Response.WriteAsJsonAsync(new { id = budget.Id }, ct);
    }

    private static async Task ListModelGroupsAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.RoutingRead) is null) return;

        var groups = await db.ModelGroups.AsNoTracking().ToListAsync(ct);
        var members = await db.ModelGroupMembers.AsNoTracking().ToListAsync(ct);
        var providers = await db.Providers.AsNoTracking().ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(groups.Select(g => new
        {
            g.Id,
            g.Name,
            g.IsAuto,
            g.AllowCrossScope,
            g.Strategy,
            members = members.Where(m => m.GroupId == g.Id).Select(m => new
            {
                provider = providers.FirstOrDefault(p => p.Id == m.ProviderId)?.Alias,
                m.UpstreamModel,
                m.Priority,
                m.Weight,
                state = m.State.ToString().ToLowerInvariant(),
                m.Enabled
            })
        }), ct);
    }

    private static async Task UpsertModelGroupAsync(
        HttpContext http, UpsertModelGroupRequest request, SessionStore sessions,
        SecureHubDbContext db, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.RoutingWrite) is null) return;
        var localizer = ApiLocalization.For(http, options);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Members.Length == 0)
        {
            await ApiError.Create(400, "validation.required", localizer).ExecuteAsync(http);
            return;
        }

        var group = await db.ModelGroups.FirstOrDefaultAsync(g => g.Name == request.Name, ct);
        if (group is null)
        {
            group = new ModelGroup
            {
                Name = request.Name,
                IsAuto = false,
                CreatedAt = clock.UtcNow
            };
            db.ModelGroups.Add(group);
        }

        group.AllowCrossScope = request.AllowCrossScope;
        group.Strategy = string.IsNullOrWhiteSpace(request.Strategy) ? "priority-weighted" : request.Strategy;
        group.Enabled = true;

        var existing = await db.ModelGroupMembers.Where(m => m.GroupId == group.Id).ToListAsync(ct);
        db.ModelGroupMembers.RemoveRange(existing);

        foreach (var member in request.Members)
        {
            var provider = await db.Providers.FindAsync(new object[] { member.ProviderId }, ct);
            if (provider is null) continue;

            db.ModelGroupMembers.Add(new ModelGroupMember
            {
                GroupId = group.Id,
                Scope = provider.Scope == ProviderScope.User ? ModelScope.User : ModelScope.Org,
                ProviderId = provider.Id,
                UpstreamModel = member.UpstreamModel,
                Priority = member.Priority,
                Weight = member.Weight <= 0 ? 100 : member.Weight,
                Enabled = member.Enabled,
                State = MemberState.Healthy
            });
        }

        await db.SaveChangesAsync(ct);
        await http.Response.WriteAsJsonAsync(new { id = group.Id, group.Name }, ct);
    }

    private static async Task QueryCatalogAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.CatalogRead) is null) return;

        var provider = http.Request.Query["provider"].ToString();
        var search = http.Request.Query["q"].ToString();

        var query = db.CatalogModels.AsNoTracking();
        if (!string.IsNullOrEmpty(provider)) query = query.Where(m => m.ProviderKey == provider);
        if (!string.IsNullOrEmpty(search)) query = query.Where(m => m.ModelId.Contains(search));

        var rows = await query.OrderBy(m => m.ProviderKey).ThenBy(m => m.ModelId).Take(500).ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(rows.Select(m => new
        {
            m.Id,
            provider = m.ProviderKey,
            model = m.ModelId,
            m.Family,
            m.SupportsTools,
            m.SupportsReasoning,
            m.ContextWindow,
            m.MaxOutputTokens,
            input_price = m.InputPricePerMillion,
            output_price = m.OutputPricePerMillion,
            m.SyncedAt
        }), ct);
    }

    private static async Task SyncCatalogAsync(
        HttpContext http, SessionStore sessions, CatalogSyncService sync,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.CatalogWrite) is null) return;

        var result = await sync.SyncAsync(ct);
        await http.Response.WriteAsJsonAsync(new
        {
            ok = result.Success,
            providers = result.Providers,
            models = result.Models,
            error = result.Error,
            synced_at = result.SyncedAt
        }, ct);
    }

    // ---------- totp ----------

    private static async Task EnableTotpAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        ProviderCredentialStore credentials, IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null)
        {
            await ApiError.NotFound(ApiLocalization.For(http, options)).ExecuteAsync(http);
            return;
        }

        var secret = Totp.GenerateSecret();
        var protectedSecret = Convert.ToBase64String(credentials.Protect(secret));
        user.TotpSecret = $"pending:{protectedSecret}";
        await db.SaveChangesAsync(ct);

        await http.Response.WriteAsJsonAsync(new
        {
            secret,
            otpauth_uri = Totp.BuildOtpAuthUri("SecureHub", user.UserName, secret),
            note = "调用 /api/auth/totp/activate 提交 6 位验证码后生效"
        }, ct);
    }

    private static async Task ActivateTotpAsync(
        HttpContext http, ActivateTotpRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user?.TotpSecret is not { } stored || !stored.StartsWith("pending:", StringComparison.Ordinal))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        var secret = credentials.Unprotect(Convert.FromBase64String(stored["pending:".Length..]));
        if (!Totp.Verify(secret, request.Code))
        {
            await ApiError.Create(400, "validation.invalid", localizer).ExecuteAsync(http);
            return;
        }

        user.TotpSecret = $"active:{stored["pending:".Length..]}";
        await db.SaveChangesAsync(ct);

        await http.Response.WriteAsJsonAsync(new { activated = true }, ct);
    }

    // ---------- audit extras ----------

    private static async Task ExportAuditAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.AuditExport) is null) return;

        var from = ParseDate(http.Request.Query["from"]) ?? DateTimeOffset.UtcNow.AddDays(-7);
        var to = ParseDate(http.Request.Query["to"]) ?? DateTimeOffset.UtcNow;

        http.Response.ContentType = "application/x-ndjson; charset=utf-8";
        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"audit-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.ndjson\"";

        var limit = int.TryParse(http.Request.Query["limit"], out var requested)
            ? Math.Clamp(requested, 1, 200_000)
            : 10_000;

        var query = db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to);

        if (!string.IsNullOrWhiteSpace(http.Request.Query["app"]))
        {
            var app = http.Request.Query["app"].ToString().Trim();
            query = query.Where(e =>
                (e.AppName != null && e.AppName.Contains(app)) ||
                (e.ActorType == "app" && e.ActorLabel.Contains(app)));
        }

        var rows = await query
            .OrderBy(e => e.Sequence)
            .Take(limit)
            .ToListAsync(ct);

        foreach (var e in rows)
        {
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                e.EventId,
                e.Sequence,
                e.OccurredAt,
                e.EventType,
                severity = e.Severity.ToString().ToLowerInvariant(),
                action = e.Action.ToString().ToLowerInvariant(),
                e.ActorType,
                e.ActorId,
                e.ActorLabel,
                e.AppId,
                app_name = e.AppName ?? (e.ActorType == "app" ? e.ActorLabel : null),
                e.TeamId,
                e.RequestId,
                e.ModelAlias,
                e.ProviderId,
                e.UpstreamModel,
                policy_id = e.PolicyId,
                policy_version = e.PolicyVersion,
                e.RuleId,
                e.ReasonCode,
                detail = e.Detail,
                e.InputTokens,
                e.InputTokensOriginal,
                e.OutputTokens,
                e.CostUsd,
                billing_owner = e.BillingOwner.ToString().ToLowerInvariant(),
                e.ContentHash,
                e.ContentMissing,
                e.PrevHash,
                e.Hash
            });

            await http.Response.WriteAsync(line + "\n", ct);
        }
    }

    private static async Task CreateDisputeAsync(
        HttpContext http, Guid eventId, CreateDisputeRequest request, SessionStore sessions,
        SecureHubDbContext db, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var localizer = ApiLocalization.For(http, options);
        if (!await db.AuditEvents.AnyAsync(e => e.EventId == eventId, ct))
        {
            await ApiError.NotFound(localizer).ExecuteAsync(http);
            return;
        }

        db.AuditEventDisputes.Add(new AuditEventDispute
        {
            EventId = eventId,
            DisputeType = string.IsNullOrWhiteSpace(request.DisputeType) ? "false_positive" : request.DisputeType,
            Note = request.Note,
            CreatedBy = session.UserId,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await http.Response.WriteAsJsonAsync(new { ok = true }, ct);
    }

    private static async Task ListDisputesAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        if (RequireAdmin(http, sessions, options.Value, Permission.AuditRead) is null) return;

        var rows = await db.AuditEventDisputes.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(rows.Select(d => new
        {
            d.Id,
            d.EventId,
            dispute_type = d.DisputeType,
            d.Note,
            d.CreatedBy,
            d.CreatedAt,
            d.ResolvedAt
        }), ct);
    }

    // ---------- me ----------
    private static async Task ListMyProvidersAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var providers = await db.Providers.AsNoTracking()
            .Where(p => p.Scope == ProviderScope.User && p.OwnerUserId == session.UserId)
            .ToListAsync(ct);

        var providerIds = providers.Select(p => p.Id).ToArray();
        var offerings = await db.ProviderOfferings.AsNoTracking()
            .Where(o => providerIds.Contains(o.ProviderId))
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(providers.Select(p => new
        {
            p.Id,
            p.Alias,
            p.ProviderKey,
            p.BaseUrl,
            models = offerings.Where(o => o.ProviderId == p.Id).Select(o => o.UpstreamModel)
        }), ct);
    }

    private static async Task CreateMyProviderAsync(
        HttpContext http, CreateProviderRequest request, SessionStore sessions,
        SecureHubDbContext db, ProviderCredentialStore credentials,
        AuditWriter audit, SystemSettingStore runtime, IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
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

        if (ModelIdReserved(request.Alias) ||
            await db.Providers.AnyAsync(p => p.Alias == request.Alias, ct))
        {
            await ApiError.Create(409, "provider_not_allowed", localizer).ExecuteAsync(http);
            return;
        }

        var provider = new Provider
        {
            Alias = request.Alias,
            ProviderKey = request.ProviderKey,
            BaseUrl = request.BaseUrl,
            DisplayName = request.Alias,
            Scope = ProviderScope.User,
            OwnerUserId = session.UserId,
            CreatedAt = clock.UtcNow
        };
        db.Providers.Add(provider);

        db.ProviderCredentials.Add(new ProviderCredential
        {
            ProviderId = provider.Id,
            OwnerUserId = session.UserId,
            CipherText = credentials.Protect(request.ApiKey),
            KeyHint = Tail(request.ApiKey),
            CreatedAt = clock.UtcNow
        });

        foreach (var model in request.Models.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderId = provider.Id,
                UpstreamModel = model,
                GroupKey = model,
                ContextWindow = 128_000,
                MaxOutputTokens = 4_096
            });
        }

        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(
            AuditEventTypes.ByokEnabled, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = $"{{\"provider\":\"{provider.Alias}\"}}", ct: ct);

        await http.Response.WriteAsJsonAsync(new { id = provider.Id, provider.Alias }, ct);
    }

    private static async Task ListMyKeysAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var keys = await db.VirtualKeys.AsNoTracking()
            .Where(k => k.OwnerType == KeyOwnerType.User && k.OwnerId == session.UserId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        await http.Response.WriteAsJsonAsync(keys.Select(k => new
        {
            id = k.Id,
            key_id = k.KeyId,
            prefix = k.Prefix,
            display_name = k.DisplayName,
            app_name = k.AppName,
            is_revoked = k.IsRevoked,
            created_at = k.CreatedAt
        }), ct);
    }

    private static async Task CreateMyKeyAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db, AuditWriter audit,
        IOptions<SecureHubOptions> options, IClock clock, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var requestedApp = http.Request.Query["appName"].ToString();
        if (string.IsNullOrWhiteSpace(requestedApp))
        {
            requestedApp = http.Request.Headers["X-SecureHub-App"].ToString();
        }

        var (fullKey, parsed) = VirtualKeyFormat.Generate();

        db.VirtualKeys.Add(new VirtualKey
        {
            KeyId = parsed.KeyId,
            SecretHash = VirtualKeyFormat.HashSecret(parsed.Secret),
            Prefix = fullKey[..Math.Min(20, fullKey.Length)],
            DisplayName = $"{session.UserName} personal",
            OwnerType = KeyOwnerType.User,
            OwnerId = session.UserId,
            AppName = NormalizeAppName(requestedApp),
            TeamId = session.TeamId,
            ScopesJson = "[\"chat:write\",\"models:read\"]",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(
            AuditEventTypes.KeyCreated, "user", session.UserId.ToString(), session.UserName,
            "ok", configure: e => e.Detail = System.Text.Json.JsonSerializer.Serialize(new
            {
                key_id = parsed.KeyId,
                owner_type = "user",
                app_name = NormalizeAppName(requestedApp)
            }), ct: ct);

        await http.Response.WriteAsJsonAsync(new { key = fullKey, key_id = parsed.KeyId }, ct);
    }

    private static async Task MyUsageAsync(
        HttpContext http, SessionStore sessions, SecureHubDbContext db,
        IOptions<SecureHubOptions> options, CancellationToken ct)
    {
        var session = RequireSession(http, sessions, options.Value);
        if (session is null) return;

        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.UserId == session.UserId && e.OccurredAt >= from)
            .ToListAsync(ct);

        var items = rows
            .GroupBy(e => e.Model)
            .Select(g => new
            {
                model = g.Key,
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                output_tokens = g.Sum(x => x.OutputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            })
            .ToList();

        await http.Response.WriteAsJsonAsync(new { from, data = items }, ct);
    }

    // ---------- helpers ----------

    private static string? NormalizeAppName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = new string(value.Trim().Where(ch => !char.IsControl(ch)).ToArray());
        return sanitized.Length == 0 ? null : sanitized.Length <= 100 ? sanitized : sanitized[..100];
    }

    private static SessionEntry? RequireSession(
        HttpContext http, SessionStore sessions, SecureHubOptions options)
    {
        var session = sessions.Get(SessionCookie.Read(http));
        if (session is not null) return session;

        ApiError.Unauthorized(ApiLocalization.For(http, Options.Create(options)))
            .ExecuteAsync(http).GetAwaiter().GetResult();
        return null;
    }

    private static SessionEntry? RequireAdmin(
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

    private static bool ModelIdReserved(string alias) =>
        ModelId.ReservedProviderAliases.Contains(alias, StringComparer.OrdinalIgnoreCase);

    private static string Tail(string value) =>
        value.Length <= 4 ? value : value[^4..];

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, out var value) ? value : null;
}
