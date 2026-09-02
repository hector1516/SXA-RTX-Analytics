using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Application.Abstractions;

public sealed record ColumnInfo(string Name, string DataType, bool IsNullable);

public sealed record DynamicQueryRequest(
    string ConnectionString,
    string TableName, // e.g. [dbo].[Registros]
    IReadOnlyList<string> Columns, // empty = *
    string? Tipo, // VTI / VTech / null
    string? Area,
    string? DeviceId, // PC-...
    DateTime? From,
    DateTime? To,
    string? DateColumn, // auto-detected
    int MaxRows = 1000);

public interface IDynamicQueryService
{
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string connectionString, string tableName, CancellationToken ct = default);
    Task<QueryResult> ExecuteAsync(DynamicQueryRequest request, CancellationToken ct = default);
}

public interface IExportService
{
    Task<byte[]> ExportExcelAsync(QueryResult result, string sheetName, CancellationToken ct = default);
    Task<byte[]> ExportPdfAsync(QueryResult result, string title, CancellationToken ct = default);
}
