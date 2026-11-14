using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SecureHub.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// 按 provider 注册专用上下文，使迁移与 provider 一一对应。
    /// 上层仍然只依赖 <see cref="SecureHubDbContext"/>。
    /// </summary>
    public static IServiceCollection AddSecureHubDbContext(
        this IServiceCollection services,
        bool usePostgres,
        string connectionString)
    {
        if (usePostgres)
        {
            services.AddDbContext<SecureHubDbContext, PostgresSecureHubDbContext>(
                o => o.UseNpgsql(connectionString));
        }
        else
        {
            services.AddDbContext<SecureHubDbContext, SqliteSecureHubDbContext>(
                o => o.UseSqlite(connectionString));
        }

        services.AddScoped<DatabaseBootstrapper>();
        return services;
    }
}
