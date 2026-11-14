using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SecureHub.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddSecureHubDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<SecureHubDbContext>(configure);
        services.AddScoped<DatabaseBootstrapper>();
        return services;
    }
}
