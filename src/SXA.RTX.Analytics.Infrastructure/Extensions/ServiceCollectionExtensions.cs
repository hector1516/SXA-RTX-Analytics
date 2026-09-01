using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;
using SXA.RTX.Analytics.Infrastructure.Services;

namespace SXA.RTX.Analytics.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ConfigurationDatabase");

        // Allow startup without a real SQL Server (e.g. CI, local scaffold). Health check will report status.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fallback to in-memory for Phase 1 scaffold so the app can boot without secrets.
            services.AddDbContext<ConfigurationDbContext>(opt =>
                opt.UseInMemoryDatabase("SXA_RTX_Analytics_Scaffold"));
        }
        else
        {
            services.AddDbContext<ConfigurationDbContext>(opt =>
                opt.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    sql.MigrationsAssembly(typeof(ConfigurationDbContext).Assembly.FullName);
                }));
        }

        services.AddScoped<ISystemTableService, SqlSystemTableService>();
        return services;
    }
}
