namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>
/// Logos que se incrustan en el reporte de trazabilidad.
/// Se pasan como bytes para no acoplar la generación al sistema de archivos.
/// </summary>
public sealed record TraceReportAssets(byte[]? AppLogo, byte[]? HussmannLogo);

/// <summary>
/// Genera un reporte PDF detallado de la trazabilidad de un número de serie.
/// </summary>
public interface ITraceReportService
{
    Task<byte[]> GeneratePdfAsync(
        string serial,
        IReadOnlyList<TraceEventDto> events,
        IReadOnlyList<TablaConfigDto> configs,
        TraceReportAssets assets,
        string? generatedBy = null,
        CancellationToken ct = default);
}
