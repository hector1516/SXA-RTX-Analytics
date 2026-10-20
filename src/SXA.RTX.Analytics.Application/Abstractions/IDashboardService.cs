namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>Actividad reciente de una tabla configurada.</summary>
public sealed record TableActivityDto(
    string TableName,
    string DisplayName,
    string Fuente,
    DateTime? LastDate,
    int RecentCount,
    int TotalCount);

/// <summary>Un punto de la serie de actividad (registros por día).</summary>
public sealed record ActivityPointDto(string Day, int Count);

/// <summary>Un registro reciente para el feed del panel.</summary>
public sealed record RecentRecordDto(
    string Serial,
    string TableName,
    string DisplayName,
    string Fuente,
    DateTime? Date);

/// <summary>Resumen de actividad para el panel de operación.</summary>
public sealed record DashboardSnapshotDto(
    DateTime? LastActivity,
    IReadOnlyList<TableActivityDto> Tables,
    IReadOnlyList<ActivityPointDto> Activity,
    IReadOnlyList<RecentRecordDto> Recent);

/// <summary>
/// Agrega la actividad reciente de las tablas operativas (de solo lectura) para
/// alimentar el panel: última actividad, registros por día y los registros más recientes.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSnapshotDto> GetSnapshotAsync(
        string? connectionString,
        IReadOnlyList<TablaConfigDto> tables,
        int days = 14,
        int recentLimit = 20,
        CancellationToken ct = default);
}
