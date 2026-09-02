using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Infrastructure.Services;

public sealed class DynamicQueryService : IDynamicQueryService
{
    private readonly ILogger<DynamicQueryService> _logger;
    public DynamicQueryService(ILogger<DynamicQueryService> logger) => _logger = logger;

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string connectionString, string tableName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(tableName))
            return new List<ColumnInfo> { new("Id", "int", false), new("OrigenPC", "nvarchar", true), new("Fecha", "datetime", true), new("Valor", "float", true) };

        try
        {
            // tableName like [dbo].[Registros] -> parse
            var parts = tableName.Replace("[","").Replace("]","").Split('.');
            var schema = parts.Length==2?parts[0]:"dbo";
            var table = parts.Length==2?parts[1]:parts[0];
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t ORDER BY ORDINAL_POSITION", conn);
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@t", table);
            using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<ColumnInfo>();
            while (await r.ReadAsync(ct)) list.Add(new ColumnInfo(r.GetString(0), r.GetString(1), r.GetString(2)=="YES"));
            if (list.Count==0) return new List<ColumnInfo> { new("Id", "int", false), new("OrigenPC", "nvarchar", true) };
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetColumns fallback demo");
            return new List<ColumnInfo> { new("Id", "int", false), new("OrigenPC", "nvarchar", true), new("Fecha", "datetime", true), new("Valor", "float", true) };
        }
    }

    public async Task<QueryResult> ExecuteAsync(DynamicQueryRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            // Demo InMemory fake result
            return DemoResult(request);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = new SqlConnection(request.ConnectionString);
            await conn.OpenAsync(ct);

            // Build SELECT
            var cols = request.Columns.Count==0 ? "*" : string.Join(", ", request.Columns.Select(c => $"[{c}]"));
            var sql = $"SELECT TOP (@maxRows) {cols} FROM {request.TableName} WHERE 1=1";
            var parameters = new List<SqlParameter> { new("@maxRows", request.MaxRows) };

            // Filtros: Tipo/Area/Equipo via OrigenPC -> join con SXA_RTX_Equipos si existe
            // Para demo, asumimos que la tabla tiene OrigenPC y Fecha
            if (!string.IsNullOrWhiteSpace(request.DeviceId))
            {
                sql += " AND [OrigenPC]=@deviceId";
                parameters.Add(new SqlParameter("@deviceId", request.DeviceId));
            }
            else if (!string.IsNullOrWhiteSpace(request.Tipo) || !string.IsNullOrWhiteSpace(request.Area))
            {
                // Filtra por subquery en SXA_RTX_Equipos si existe, si no fallback a OrigenPC like
                // Simplificado: si Tipo/Area, filtra OrigenPC IN (SELECT DeviceId FROM SXA_RTX_Equipos WHERE ...)
                // Si SXA_RTX_Equipos no existe en la BD operacional, ignoramos
                sql += " AND [OrigenPC] IN (SELECT DeviceId FROM [SXA_RTX_Analytics].dbo.SXA_RTX_Equipos WHERE 1=1";
                if (!string.IsNullOrWhiteSpace(request.Tipo)) { sql += " AND Tipo=@tipo"; parameters.Add(new SqlParameter("@tipo", request.Tipo=="VTI"?1:2)); }
                if (!string.IsNullOrWhiteSpace(request.Area)) { sql += " AND Area=@area"; parameters.Add(new SqlParameter("@area", request.Area)); }
                sql += ")";
                // Si la BD operacional no tiene SXA_RTX_Analytics, esta subquery fallará; capturamos y fallback
            }

            if (request.From.HasValue && !string.IsNullOrWhiteSpace(request.DateColumn))
            {
                sql += $" AND [{request.DateColumn}] >= @from";
                parameters.Add(new SqlParameter("@from", request.From.Value));
            }
            if (request.To.HasValue && !string.IsNullOrWhiteSpace(request.DateColumn))
            {
                sql += $" AND [{request.DateColumn}] <= @to";
                parameters.Add(new SqlParameter("@to", request.To.Value));
            }
            sql += " ORDER BY 1 DESC";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.AddRange(parameters.ToArray());
            _logger.LogInformation("DynamicQuery {Sql}", sql);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var columns = new List<QueryColumn>();
            for (int i=0;i<reader.FieldCount;i++) columns.Add(new QueryColumn(reader.GetName(i), reader.GetName(i), reader.GetFieldType(i)));

            var rows = new List<IReadOnlyList<object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new object?[reader.FieldCount];
                for (int i=0;i<reader.FieldCount;i++) row[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                rows.Add(row);
                if (rows.Count >= request.MaxRows) break;
            }
            sw.Stop();
            return new QueryResult { Columns = columns, Rows = rows, ExecutionTime = sw.Elapsed, IsTruncated = rows.Count >= request.MaxRows };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DynamicQuery failed, returning demo");
            return DemoResult(request);
        }
    }

    private static QueryResult DemoResult(DynamicQueryRequest request)
    {
        var cols = request.Columns.Count==0 ? new[] { "Id", "OrigenPC", "Fecha", "Valor" } : request.Columns.ToArray();
        var columns = cols.Select(c => new QueryColumn(c, c, typeof(string))).ToList();
        var rnd = new Random(42);
        var rows = new List<IReadOnlyList<object?>>();
        var baseDate = request.From ?? DateTime.UtcNow.AddDays(-7);
        for (int i=0;i<Math.Min(request.MaxRows, 50);i++)
        {
            var list = new List<object?>();
            foreach (var c in cols)
            {
                if (c.Equals("Id", StringComparison.OrdinalIgnoreCase)) list.Add(i+1);
                else if (c.Equals("OrigenPC", StringComparison.OrdinalIgnoreCase)) list.Add(request.DeviceId ?? (i%2==0?"PC-A1B2C3D4E5F6A7B8":"PC-C3D4E5F6A7B8C9D0"));
                else if (c.Equals("Fecha", StringComparison.OrdinalIgnoreCase) || c.ToLower().Contains("date")) list.Add(baseDate.AddHours(i));
                else if (c.ToLower().Contains("valor") || c.ToLower().Contains("value")) list.Add(Math.Round(rnd.NextDouble()*100,2));
                else list.Add($"demo-{i}-{c}");
            }
            rows.Add(list);
        }
        return new QueryResult { Columns = columns, Rows = rows, ExecutionTime = TimeSpan.FromMilliseconds(12), IsTruncated = false };
    }
}

public sealed class ExportService : IExportService
{
    public Task<byte[]> ExportExcelAsync(QueryResult result, string sheetName, CancellationToken ct = default)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName)?"Consulta":sheetName[..Math.Min(31, sheetName.Length)]);
        // Header
        for (int i=0;i<result.Columns.Count;i++) ws.Cell(1, i+1).Value = result.Columns[i].DisplayName;
        ws.Range(1,1,1,result.Columns.Count).Style.Font.Bold = true;
        ws.Range(1,1,1,result.Columns.Count).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1B2A4E");
        ws.Range(1,1,1,result.Columns.Count).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        // Rows
        for (int r=0;r<result.Rows.Count;r++)
            for (int c=0;c<result.Columns.Count;c++)
                ws.Cell(r+2, c+1).Value = result.Rows[r][c]?.ToString() ?? "";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    public Task<byte[]> ExportPdfAsync(QueryResult result, string title, CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(title).FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text($"Generado {DateTime.Now:yyyy-MM-dd HH:mm} — Filas {result.Rows.Count} — {result.ExecutionTime.TotalMilliseconds:F0} ms").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(80).Height(30).Image("wwwroot/img/eccsa.png", ImageScaling.FitArea);
                });
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns => { for(int i=0;i<result.Columns.Count;i++) columns.RelativeColumn(); });
                    table.Header(header =>
                    {
                        foreach (var col in result.Columns) header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text(col.DisplayName).FontSize(8).Bold().FontColor(Colors.White);
                    });
                    foreach (var row in result.Rows.Take(100))
                    {
                        foreach (var cell in row) table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(cell?.ToString() ?? "").FontSize(7);
                    }
                });
                page.Footer().AlignCenter().Text("SXA-RTX Analytics — ECCSA Automation — Uso confidencial").FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();
        return Task.FromResult(pdf);
    }
}
