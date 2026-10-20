using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Agrega la actividad reciente de las tablas operativas (solo lectura) para el panel.
/// Las fechas se guardan como texto ISO (yyyy-MM-dd[ HH:mm:ss]), así que se comparan
/// como cadenas para evitar TRY_CONVERT sobre millones de filas.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(ILogger<DashboardService> logger) => _logger = logger;

    public async Task<DashboardSnapshotDto> GetSnapshotAsync(
        string? connectionString,
        IReadOnlyList<TablaConfigDto> tables,
        int days = 14,
        int recentLimit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || tables.Count == 0)
            return new DashboardSnapshotDto(null, Array.Empty<TableActivityDto>(), Array.Empty<ActivityPointDto>(), Array.Empty<RecentRecordDto>());

        var cutoff = DateTime.Now.Date.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
        var active = tables
            .Where(t => !string.IsNullOrWhiteSpace(t.TableName) && !string.IsNullOrWhiteSpace(t.DateColumn))
            .ToList();

        var tableRows = new List<TableActivityDto>();
        var dayCounts = new Dictionary<DateTime, int>();
        var recent = new List<RecentRecordDto>();

        foreach (var t in active)
        {
            try
            {
                var tableSql = QuoteTable(t.TableName);
                var dateCol = QuoteIdent(t.DateColumn!);
                var display = string.IsNullOrWhiteSpace(t.DisplayName) ? t.TableName : t.DisplayName!;

                // Última fecha + conteo reciente + total (una sola pasada, comparación por cadena).
                var agg = await QueryRowsAsync(connectionString,
                    $"SELECT MAX({dateCol}) AS last, " +
                    $"SUM(CASE WHEN {dateCol} >= @cutoff AND LEN({dateCol}) >= 10 THEN 1 ELSE 0 END) AS recent, " +
                    $"COUNT(*) AS total FROM {tableSql}",
                    cmd => cmd.Parameters.Add(new SqlParameter("@cutoff", cutoff)), ct);

                var row = agg.FirstOrDefault();
                var last = ToDate(row?[0]);
                var recentCount = Convert.ToInt32(row?[1] ?? 0);
                var total = Convert.ToInt32(row?[2] ?? 0);
                tableRows.Add(new TableActivityDto(t.TableName, display, t.Fuente, last, recentCount, total));

                // Solo agrega series y feed para tablas con actividad reciente (evita escanear inactivas).
                if (recentCount > 0)
                {
                    var buckets = await QueryRowsAsync(connectionString,
                        $"SELECT LEFT({dateCol}, 10) AS day, COUNT(*) AS n FROM {tableSql} " +
                        $"WHERE {dateCol} >= @cutoff AND LEN({dateCol}) >= 10 GROUP BY LEFT({dateCol}, 10)",
                        cmd => cmd.Parameters.Add(new SqlParameter("@cutoff", cutoff)), ct);

                    foreach (var b in buckets)
                    {
                        var d = ToDate(b?[0]);
                        if (d is null) continue;
                        var n = Convert.ToInt32(b?[1] ?? 0);
                        dayCounts[d.Value.Date] = dayCounts.GetValueOrDefault(d.Value.Date) + n;
                    }

                    if (!string.IsNullOrWhiteSpace(t.SerialColumn))
                    {
                        var serialCol = QuoteIdent(t.SerialColumn!);
                        var rows = await QueryRowsAsync(connectionString,
                            $"SELECT TOP 3 {serialCol}, {dateCol} FROM {tableSql} " +
                            $"WHERE {dateCol} >= @cutoff AND LEN({dateCol}) >= 10 ORDER BY {dateCol} DESC",
                            cmd => cmd.Parameters.Add(new SqlParameter("@cutoff", cutoff)), ct);

                        foreach (var r in rows)
                        {
                            var serial = r?[0]?.ToString();
                            var date = ToDate(r?[1]);
                            if (string.IsNullOrWhiteSpace(serial)) continue;
                            recent.Add(new RecentRecordDto(serial, t.TableName, display, t.Fuente, date));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Panel: no se pudo leer actividad de {Table}", t.TableName);
            }
        }

        var activity = BuildActivity(days, dayCounts);
        var recentTop = recent
            .OrderByDescending(r => r.Date ?? DateTime.MinValue)
            .ThenByDescending(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            .Take(recentLimit)
            .ToList();

        return new DashboardSnapshotDto(
            tableRows.Where(t => t.LastDate.HasValue).Select(t => t.LastDate).Max(),
            tableRows.OrderByDescending(t => t.LastDate ?? DateTime.MinValue).ToList(),
            activity,
            recentTop);
    }

    private static List<ActivityPointDto> BuildActivity(int days, Dictionary<DateTime, int> counts)
    {
        var today = DateTime.Now.Date;
        var result = new List<ActivityPointDto>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            result.Add(new ActivityPointDto(day.ToString("MM-dd"), counts.GetValueOrDefault(day)));
        }
        return result;
    }

    private static async Task<List<object?[]>> QueryRowsAsync(string cs, string sql, Action<SqlCommand> configure, CancellationToken ct)
    {
        var rows = new List<object?[]>();
        using var conn = SqlProbe.Create(cs);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 45 };
        configure(cmd);
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var row = new object?[r.FieldCount];
            r.GetValues(row);
            rows.Add(row);
        }
        return rows;
    }

    private static DateTime? ToDate(object? value)
    {
        if (value is null || value is DBNull) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string QuoteTable(string table) =>
        string.Join(".", table.Replace("[", "").Replace("]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => "[" + p + "]"));

    private static string QuoteIdent(string ident) => "[" + ident.Trim().Trim('[', ']') + "]";
}
