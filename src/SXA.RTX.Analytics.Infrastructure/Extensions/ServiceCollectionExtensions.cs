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
        // La base SQL de producción es SOLO LECTURA: la app nunca crea ni modifica
        // objetos en ella. La configuración (usuarios, fuentes, nicknames, ajustes)
        // vive en un archivo SQLite local, junto a la aplicación.
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "sxa-config.db");

        services.AddDbContext<ConfigurationDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<SecretProtector>();
        services.AddScoped<IConfigurationService, ApplicationSettingService>();
        services.AddScoped<IDataSourceService, DataSourceService>();
        services.AddScoped<ISystemTableService, SqlSystemTableService>();
        services.AddScoped<IEquipoService, EquipoService>();
        services.AddScoped<ITablasConfigService, TablasConfigService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<BootstrapAdminService>();
        services.AddScoped<ConfigSeeder>();
        services.AddScoped<IConfigExportService, ConfigExportService>();
        services.AddScoped<IDynamicQueryService, DynamicQueryService>();
        services.AddScoped<ITraceService, TraceService>();
        services.AddScoped<IExportService, ExportService>();
        return services;
    }
}
