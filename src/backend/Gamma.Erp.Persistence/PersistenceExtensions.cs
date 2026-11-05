using Gamma.Erp.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gamma.Erp.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Registers <see cref="ErpDbContext"/> on PostgreSQL with the tenant isolation and the history rules.</summary>
    public static IServiceCollection AddErpPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<HistoryInterceptor>();
        services.AddDbContext<ErpDbContext>((sp, options) => Configure(options, connectionString)
            .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>(), sp.GetRequiredService<HistoryInterceptor>()));
        return services;
    }

    /// <summary>The options shared by the application and the design-time factory, so migrations match what runs.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(ErpDbContext.MigrationsAssembly)).UseSnakeCaseNamingConvention();
    }
}
