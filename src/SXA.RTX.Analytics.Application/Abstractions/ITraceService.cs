namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>
/// Busca un número de serie en varias tablas operativas y devuelve los eventos
/// ordenados por fecha para dibujar una línea de tiempo.
/// </summary>
public interface ITraceService
{
    Task<IReadOnlyList<TraceEventDto>> SearchAsync(
        string? connectionString,
        string serial,
        IReadOnlyList<TraceTableSpec> tables,
        DateTime? from = null,
        DateTime? to = null,
        int maxPerTable = 500,
        CancellationToken ct = default);
}