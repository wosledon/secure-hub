using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SecureHub.Core.Domain;
using SecureHub.Core.Security;

namespace SecureHub.Data;

public sealed record BootstrapResult(string AdminUserName, string? GeneratedPassword);

/// <summary>建库/迁移与首次种子数据。</summary>
public sealed class DatabaseBootstrapper(
    SecureHubDbContext db,
    ILogger<DatabaseBootstrapper> logger)
{
    public async Task<BootstrapResult> InitializeAsync(CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        if (pending.Length > 0)
        {
            logger.LogInformation("Applying {Count} migrations", pending.Length);
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (await db.Users.AnyAsync(ct))
        {
            return new BootstrapResult("admin", null);
        }

        var team = new Team { Name = "Default", CreatedAt = DateTimeOffset.UtcNow };
        db.Teams.Add(team);

        var app = new App
        {
            Name = "demo-app",
            TeamId = team.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Apps.Add(app);

        var generatedPassword = PasswordHasher.GeneratePassword();
        var admin = new User
        {
            UserName = "admin",
            PasswordHash = PasswordHasher.Hash(generatedPassword),
            TeamId = team.Id,
            RolesJson = "[\"PlatformAdmin\",\"SecurityAdmin\",\"TeamOwner\",\"Developer\",\"Auditor\"]",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(admin);

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded initial admin account '{User}'. One-time password: {Password} (change it immediately).",
            admin.UserName, generatedPassword);

        return new BootstrapResult(admin.UserName, generatedPassword);
    }
}
