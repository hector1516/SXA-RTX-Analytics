using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Trazabilidad de un número de serie: consulta cada tabla configurada con columna
/// de serie y de fecha, y devuelve los eventos ordenados cronológicamente.
/// Es agnóstico al tipo: filtra/ordena con TRY_CONVERT(datetime2, ...) para funcionar
/// tanto con columnas datetime como con texto ISO.
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

                var sql = $"SELECT TOP (@max) * FROM {tableSql} WHERE {serialCol} = @serial";
                if (dateCol is not null)
                {
                    sql += $" AND (@fromD IS NULL OR TRY_CONVERT(datetime2, {dateCol}) >= @fromD)" +
                           $" AND (@toD IS NULL OR TRY_CONVERT(datetime2, {dateCol}) <= @toD)";
                    sql += $" ORDER BY TRY_CONVERT(datetime2, {dateCol})";
                }

                using var conn = SqlProbe.Create(connectionString);
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
                    events.Add(new TraceEventDto(t.Fuente, t.TableName, t.TableDisplayName, occurred, values));
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