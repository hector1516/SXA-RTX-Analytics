using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Trazabilidad de un número de serie: consulta cada tabla configurada con columna
/// de serie y de fecha, y devuelve los eventos ordenados cronológicamente.
///
/// Si la tabla tiene una tabla de detalle relacionada (p. ej. VTi_UutRecords ->
/// VTi_UutRecordDetails), adjunta sus filas a cada evento para mostrar los resultados
/// de las pruebas. Es agnóstico al tipo: filtra/ordena con TRY_CONVERT(datetime2, ...).
/// </summary>
public sealed class TraceService : ITraceService
{
    private readonly ILogger<TraceService> _logger;

    public TraceService(ILogger<TraceService> logger) => _logger = logger;

    public async Task<IReadOnlyList<TraceEventDto>> SearchAsync(
        string? connectionString,
        string serial,
        IReadOnlyList<TraceTableSpec> tables,
        DateTime? from = null,
        DateTime? to = null,
        int maxPerTable = 500,
        CancellationToken ct = default)
    {
        var events = new List<TraceEventDto>();
        if (string.IsNullOrWhiteSpace(connectionString) || tables.Count == 0 || string.IsNullOrWhiteSpace(serial))
            return events;

        foreach (var t in tables)
        {
            if (string.IsNullOrWhiteSpace(t.TableName) || string.IsNullOrWhiteSpace(t.SerialColumn))
                continue;

            try
            {
                var tableSql = QuoteTable(t.TableName);
                var serialCol = QuoteIdent(t.SerialColumn);
                var dateCol = string.IsNullOrWhiteSpace(t.DateColumn) ? null : QuoteIdent(t.DateColumn);
                var detalle = t.Detalle ?? AutoDetail(t.TableName);

                var sql = $"SELECT TOP (@max) * FROM {tableSql} WHERE {serialCol} = @serial";
                if (dateCol is not null)
                {
                    sql += $" AND (@fromD IS NULL OR TRY_CONVERT(datetime2, {dateCol}) >= @fromD)" +
                           $" AND (@toD IS NULL OR TRY_CONVERT(datetime2, {dateCol}) <= @toD)";
                    sql += $" ORDER BY TRY_CONVERT(datetime2, {dateCol})";
                }

                // 1) Leer el registro padre.
                var parents = new List<(IReadOnlyDictionary<string, string?> Values, DateTime? Date, string? Key)>();
                using (var conn = SqlProbe.Create(connectionString))
                {
                    await conn.OpenAsync(ct);
                    using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
                    cmd.Parameters.Add(new SqlParameter("@max", maxPerTable));
                    cmd.Parameters.Add(new SqlParameter("@serial", serial));
                    cmd.Parameters.Add(new SqlParameter("@fromD", SqlDbType.DateTime2) { Value = (object?)from ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@toD", SqlDbType.DateTime2) { Value = (object?)to ?? DBNull.Value });

                    using var r = await cmd.ExecuteReaderAsync(ct);
                    var names = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
                    while (await r.ReadAsync(ct))
                    {
                        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        DateTime? occurred = null;
                        for (var i = 0; i < r.FieldCount; i++)
                        {
                            var isNull = await r.IsDBNullAsync(i, ct);
                            var raw = isNull ? null : r.GetValue(i);
                            values[names[i]] = raw?.ToString();
                            if (dateCol is not null && string.Equals(names[i], t.DateColumn, StringComparison.OrdinalIgnoreCase))
                                occurred = ToDate(raw);
                        }
                        string? key = null;
                        if (detalle is not null && !string.IsNullOrWhiteSpace(detalle.ParentKey))
                            values.TryGetValue(detalle.ParentKey!, out key);
                        parents.Add((values, occurred, key));
                    }
                }

                // 2) Leer el detalle relacionado (resultados de pruebas).
                var detailsByKey = new Dictionary<string, List<IReadOnlyDictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
                if (detalle is not null && !string.IsNullOrWhiteSpace(detalle.Table) && !string.IsNullOrWhiteSpace(detalle.ForeignKey))
                    detailsByKey = await LoadDetailsAsync(connectionString, detalle, parents.Select(p => p.Key), ct);

                // 3) Construir los eventos con su detalle.
                foreach (var p in parents)
                {
                    List<IReadOnlyDictionary<string, string?>>? detail = null;
                    if (p.Key is not null && detailsByKey.TryGetValue(p.Key, out var list)) detail = list;
                    events.Add(new TraceEventDto(t.Fuente, t.TableName, t.TableDisplayName, p.Date, p.Values, detail));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trazabilidad: no se pudo leer {Table}", t.TableName);
            }
        }

        return events
            .OrderBy(e => e.OccurredAt ?? DateTime.MaxValue)
            .ThenBy(e => e.Fuente, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, List<IReadOnlyDictionary<string, string?>>>> LoadDetailsAsync(
        string connectionString, TablaDetalleDto detalle, IEnumerable<string?> keys, CancellationToken ct)
    {
        var result = new Dictionary<string, List<IReadOnlyDictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var distinct = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!).Distinct(StringComparer.OrdinalIgnoreCase).Take(900).ToList();
        if (distinct.Count == 0) return result;

        var inList = string.Join(", ", distinct.Select((_, i) => $"@k{i}"));
        var sql = $"SELECT * FROM {QuoteTable(detalle.Table!)} WHERE {QuoteIdent(detalle.ForeignKey!)} IN ({inList})";

        using var conn = SqlProbe.Create(connectionString);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        for (var i = 0; i < distinct.Count; i++) cmd.Parameters.Add(new SqlParameter($"@k{i}", distinct[i]));

        using var r = await cmd.ExecuteReaderAsync(ct);
        var names = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < r.FieldCount; i++)
                row[names[i]] = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i)?.ToString();

            row.TryGetValue(detalle.ForeignKey!, out var fk);
            if (string.IsNullOrWhiteSpace(fk)) continue;
            if (!result.TryGetValue(fk, out var list)) { list = new List<IReadOnlyDictionary<string, string?>>(); result[fk] = list; }
            list.Add(row);
        }
        return result;
    }

    /// <summary>Relación conocida: los resultados de pruebas VTI viven en el detalle.</summary>
    private static TablaDetalleDto? AutoDetail(string table)
    {
        if (table.Contains("VTi_UutRecords", StringComparison.OrdinalIgnoreCase))
            return new TablaDetalleDto("dbo.VTi_UutRecordDetails", "UutRecordID", "ID", "DateTime", "Test", "Result", "Value", "Units");
        return null;
    }

    private static DateTime? ToDate(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    /// <summary>Acepta "[dbo].[Tabla]" o "dbo.Tabla" o "Tabla".</summary>
    private static string QuoteTable(string table) =>
        string.Join(".", table.Replace("[", "").Replace("]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => "[" + p + "]"));

    private static string QuoteIdent(string ident) =>
        "[" + ident.Trim().Trim('[', ']').Replace("]", "") + "]";
}