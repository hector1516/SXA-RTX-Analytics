using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Resuelve las conexiones efectivas.
/// Precedencia: valor guardado en la base de configuracion > appsettings > variable de entorno.
/// La variable de entorno gana sobre appsettings porque es lo que se usa en produccion (IIS).
/// </summary>
public sealed class DataSourceService : IDataSourceService
{
    private readonly IConfigurationService _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<DataSourceService> _logger;

    public DataSourceService(IConfigurationService settings, IConfiguration config, ILogger<DataSourceService> logger)
    {
        _settings = settings;
        _config = config;
        _logger = logger;
    }

    public async Task<DataSourceSettings> GetAsync(CancellationToken ct = default)
    {
        var storedConfig = await _settings.GetValueAsync(SettingsKeys.SqlConfiguration, ct);
        var storedOperational = await _settings.GetValueAsync(SettingsKeys.SqlOperational, ct);
        var storedOdbc = await _settings.GetValueAsync(SettingsKeys.OdbcMapics, ct);

        // IConfiguration ya incluye variables de entorno con doble guion bajo, asi que
        // _config["ConnectionStrings__X"] devuelve null si no esta definida.
        return new DataSourceSettings(
            First(storedConfig, _config.GetConnectionString("ConfigurationDatabase"), _config["ConnectionStrings__ConfigurationDatabase"]),
            First(storedOperational, _config.GetConnectionString("OperationalDatabase"), _config["ConnectionStrings__OperationalDatabase"]),
            First(storedOdbc, _config.GetConnectionString("MapicsOdbc"), _config["ConnectionStrings__MapicsOdbc"]));
    }

    public async Task SaveAsync(string? configurationConnectionString, string? operationalConnectionString, string? odbcConnectionString, CancellationToken ct = default)
    {
        await SaveOneAsync(SettingsKeys.SqlConfiguration, configurationConnectionString, ct);
        await SaveOneAsync(SettingsKeys.SqlOperational, operationalConnectionString, ct);
        await SaveOneAsync(SettingsKeys.OdbcMapics, odbcConnectionString, ct);
    }

    private async Task SaveOneAsync(string key, string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Cadena vacia = borrar el override guardado y volver a appsettings/entorno.
            await _settings.SetValueAsync(key, "", ct);
            return;
        }
        await _settings.SetValueAsync(key, value.Trim(), ct);
        _logger.LogInformation("Conexion guardada en {Key}", key);   // nunca se registra el valor
    }

    private string? First(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}