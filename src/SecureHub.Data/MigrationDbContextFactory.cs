using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SecureHub.Data;

/// <summary>
/// 设计时工厂：`dotnet ef` 不启动宿主。
/// 两个 context 各自持有迁移目录，命令需指定 -c。
/// </summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteSecureHubDbContext>
{
    public SqliteSecureHubDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SqliteSecureHubDbContext>();
        builder.UseSqlite("Data Source=securehub-design.db");
        return new SqliteSecureHubDbContext(builder.Options);
    }
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresSecureHubDbContext>
{
    public PostgresSecureHubDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PostgresSecureHubDbContext>();
        builder.UseNpgsql("Host=localhost;Database=securehub;Username=postgres;Password=postgres");
        return new PostgresSecureHubDbContext(builder.Options);
    }
}
