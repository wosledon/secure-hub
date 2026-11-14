using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SecureHub.Data;

/// <summary>
/// 设计时工厂：`dotnet ef` 不启动宿主，避免读到运行配置。
/// 通过环境变量选择 provider：SH_MIGRATION_PROVIDER=sqlite|postgres。
/// </summary>
public sealed class MigrationDbContextFactory : IDesignTimeDbContextFactory<SecureHubDbContext>
{
    public SecureHubDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("SH_MIGRATION_PROVIDER") ?? "sqlite";
        var builder = new DbContextOptionsBuilder<SecureHubDbContext>();

        if (string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql(
                "Host=localhost;Database=securehub;Username=postgres;Password=postgres");
        }
        else
        {
            builder.UseSqlite("Data Source=securehub-design.db");
        }

        return new SecureHubDbContext(builder.Options);
    }
}
