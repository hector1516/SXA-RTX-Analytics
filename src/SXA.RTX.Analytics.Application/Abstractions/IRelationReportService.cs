namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>
/// Relación descubierta entre dos tablas (padre -> hijo).
/// Se busca por <see cref="ParentSearchColumn"/> (número de serie si la hay)
/// y se une con <see cref="ParentKeyColumn"/> = <see cref="ChildKeyColumn"/>.
/// </summary>
public sealed record TableRelationDto(
    string ParentTable,
    string ParentKeyColumn,
    string ParentSearchColumn,
    string ChildTable,
    string ChildKeyColumn,
    string Category,
    string Title);

/// <summary>Resultado de ejecutar una relación: filas del padre y del hijo.</summary>
public sealed record RelationResultDto(
    IReadOnlyList<string> ParentColumns,
    IReadOnlyList<IReadOnlyList<string?>> ParentRows,
    IReadOnlyList<string> ChildColumns,
    IReadOnlyList<IReadOnlyList<string?>> ChildRows,
    int ParentTotal,
    int ChildTotal);

/// <summary>
/// Descubre relaciones entre tablas de la base operativa (de solo lectura) y
/// permite ejecutarlas como reportes, además de exportarlas a CSV/PDF.
/// </summary>
public interface IRelationReportService
{
    Task<IReadOnlyList<TableRelationDto>> DiscoverAsync(string? connectionString, CancellationToken ct = default);
    Task<RelationResultDto> RunAsync(string? connectionString, TableRelationDto relation, string key, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(TableRelationDto relation, RelationResultDto result, string key, CancellationToken ct = default);
    Task<byte[]> ExportPdfAsync(TableRelationDto relation, RelationResultDto result, string key, CancellationToken ct = default);
}
