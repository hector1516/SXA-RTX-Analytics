using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Persistencia key-value sobre SXA_RTX_ApplicationSettings.
/// Los valores marcados como secretos se guardan cifrados con DPAPI.
/// </summary>
public sealed class ApplicationSettingService : IConfigurationService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<ApplicationSettingService> _logger;
    private readonly SecretProtector _protector;

    public ApplicationSettingService(ConfigurationDbContext db, ILogger<ApplicationSettingService> logger, SecretProtector protector)
    {
        _db = db;
        _logger = logger;
        _protector = protector;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.Set<ApplicationSetting>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return null;
        var value = row.IsEncrypted ? _protector.Unprotect(row.Value) : row.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("La clave es obligatoria.", nameof(key));
        var row = await _db.Set<ApplicationSetting>().FirstOrDefaultAsync(x => x.Key == key, ct);

        // Solo se cifran los secretos (cadenas de conexion). Un ajuste normal como
        // App.UpdateCheckMinutes debe quedar legible para la UI y para el export.
        var sensible = IsSensitiveKey(key);
        var stored = value;
        var encrypted = false;
        if (sensible && !string.IsNullOrEmpty(value))
        {
            encrypted = _protector.TryProtect(value, out var protectedValue);
            stored = protectedValue;
        }

        if (row is null)
        {
            _db.Set<ApplicationSetting>().Add(new ApplicationSetting
            {
                Key = key.Trim(),
                Value = stored,
                IsEncrypted = encrypted,
                Category = "Configuración",
                Description = DescriptionFor(key),
            });
        }
        else
        {
            row.Value = stored;
            row.IsEncrypted = encrypted;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        if (sensible && !encrypted && !string.IsNullOrEmpty(value))
            _logger.LogWarning("El valor de {Key} se guardo sin cifrar porque DPAPI no respondio", key);
    }

    /// <summary>Las claves de conexión se tratan como secreto; el resto es configuración legible.</summary>
    public static bool IsSensitiveKey(string key) =>
        key.StartsWith("Sql.ConnectionString.", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Odbc.ConnectionString.", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ConfigurationItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.Set<ApplicationSetting>().AsNoTracking().OrderBy(x => x.Key).ToListAsync(ct);
        return rows.Select(x => new ConfigurationItemDto(
            x.Id,
            x.Key,
            x.IsEncrypted ? "********" : x.Value,   // nunca se expone un secreto en la UI
            x.Description,
            x.Category,
            x.CreatedAtUtc)).ToList();
    }

    private static string? DescriptionFor(string key) => key switch
    {
        SettingsKeys.SqlConfiguration => "Cadena de conexion de la base de configuracion SXA_RTX_Analytics",
        SettingsKeys.SqlOperational => "Cadena de conexion de la base operacional (tablas VTI/VTech)",
        SettingsKeys.OdbcMapics => "Cadena ODBC de MAPICS",
        SettingsKeys.OperationalSourceName => "Nombre del origen operacional mostrado en la UI",
        SettingsKeys.UpdateCheckMinutes => "Minutos entre comprobaciones de actualizacion",
        _ => null,
    };
}