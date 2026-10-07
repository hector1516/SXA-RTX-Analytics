namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>
/// Claves de <see cref="IConfigurationService"/> con significado para la aplicacion.
/// </summary>
public static class SettingsKeys
{
    /// <summary>Cadena de conexion de la base de configuracion (SXA_RTX_Analytics).</summary>
    public const string SqlConfiguration = "Sql.ConnectionString.Configuration";

    /// <summary>
    /// Cadena de conexion de la base OPERACIONAL (donde viven las tablas VTI/VTech).
    /// Si no se define, /query usa la base de configuracion.
    /// </summary>
    public const string SqlOperational = "Sql.ConnectionString.Operational";

    /// <summary>Cadena ODBC de MAPICS.</summary>
    public const string OdbcMapics = "Odbc.ConnectionString.Mapics";

    /// <summary>Nombre del origen de datos shown en la cabecera de /query.</summary>
    public const string OperationalSourceName = "Sql.OperationalSourceName";

    /// <summary>Minutos entre comprobaciones periodicas de actualizacion (0 = solo al cargar).</summary>
    public const string UpdateCheckMinutes = "App.UpdateCheckMinutes";
}

/// <summary>Conexiones efectivas que usa la aplicacion.</summary>
public sealed record DataSourceSettings(
    string? ConfigurationConnectionString,
    string? OperationalConnectionString,
    string? OdbcConnectionString)
{
    /// <summary>True cuando no hay operacion configurada y se cae a la base de configuracion.</summary>
    public bool OperationalFallsBackToConfiguration =>
        string.IsNullOrWhiteSpace(OperationalConnectionString);

    /// <summary>Conexion con la que se ejecutan las consultas de las tablas VTI/VTech.</summary>
    public string? EffectiveOperationalConnectionString =>
        string.IsNullOrWhiteSpace(OperationalConnectionString)
            ? ConfigurationConnectionString
            : OperationalConnectionString;
}

public interface IDataSourceService
{
    /// <summary>Lee las conexiones guardadas y aplica los valores de appsettings/entorno como respaldo.</summary>
    Task<DataSourceSettings> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Guarda las conexiones indicadas en la base de configuracion.
    /// Las cadenas vacias o nulas eliminan la clave.
    /// </summary>
    Task SaveAsync(string? configurationConnectionString, string? operationalConnectionString, string? odbcConnectionString, CancellationToken ct = default);
}