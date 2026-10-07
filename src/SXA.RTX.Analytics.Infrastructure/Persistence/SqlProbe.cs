using Microsoft.Data.SqlClient;

namespace SXA.RTX.Analytics.Infrastructure.Persistence;

/// <summary>
/// Limita el tiempo de espera de las conexiones SQL de la plataforma.
/// Sin esto, si el servidor no esta disponible la interfaz se queda colgada
/// durante el login timeout por defecto (15-30 s) antes del fallback a demo.
/// </summary>
public static class SqlProbe
{
    public const int ConnectTimeoutSeconds = 5;
    public const int CommandTimeoutSeconds = 15;

    /// <summary>Crea una conexion con timeout de conexion y de comando acotados.</summary>
    public static SqlConnection Create(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = ConnectTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds,
        };
        return new SqlConnection(builder.ConnectionString);
    }

    /// <summary>Token que se cancela tras <paramref name="seconds"/> o cuando se cancela el token padre.</summary>
    public static CancellationTokenSource WithTimeout(CancellationToken ct, int seconds = ConnectTimeoutSeconds + 5)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }
}