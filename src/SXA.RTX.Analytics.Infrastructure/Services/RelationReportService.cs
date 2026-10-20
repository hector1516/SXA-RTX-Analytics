using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Descubre relaciones entre tablas de la base operativa (de solo lectura) a
/// partir del esquema (nombres de columnas) y las ejecuta como reportes.
/// - Uniones por clave: columna hija "XxxID" apuntando a la tabla padre con columna "ID".
/// - Uniones por columna compartida: misma columna en varias tablas (dimensión -> hecho).
/// </summary>
public sealed class RelationReportService : IRelationReportService
{
    private static readonly string[] SerialHints =
        { "serial", "serie", "serial_number", "serialno", "serialnum", "serial_num" };

    public async Task<IReadOnlyList<TableRelationDto>> DiscoverAsync(string? connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return Array.Empty<TableRelationDto>();

        var cols = await LoadColumnsAsync(connectionString, ct);
        if (cols.Count == 0) return Array.Empty<TableRelationDto>();

        var tables = cols.Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var idTables = cols.Where(c => IsId(c.Column)).Select(c => c.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var colCount = cols.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var relations = new List<TableRelationDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string parent, string parentKey, string searchCol, string child, string childKey)
        {
            var key = $"{parent}|{parentKey}|{child}|{childKey}";
            if (!seen.Add(key)) return;
            relations.Add(new TableRelationDto(parent, parentKey, searchCol, child, childKey, Category(parent), Title(parent, child)));
        }

        // 1) Uniones por clave tipo "UutRecordID" -> tabla padre con columna "ID".
        foreach (var child in cols)
        {
            var col = child.Column;
            if (col.Length <= 2 || !col.EndsWith("ID", StringComparison.OrdinalIgnoreCase) || IsId(col)) continue;
            var baseName = col[..^2];
            var parent = tables.FirstOrDefault(t =>
                idTables.Contains(t) &&
                !string.Equals(t, child.Table, StringComparison.OrdinalIgnoreCase) &&
                Normalize(t) == Normalize(baseName));
            if (parent is null) continue;
            Add(parent, "ID", SerialColumn(cols, parent) ?? "ID", child.Table, col);
        }

        // 2) Uniones por columna compartida significativa (dimensión -> hecho).
        foreach (var shared in new[] { "ModelNo", "Tag" })
        {
            var distinct = cols
                .Where(c => string.Equals(c.Column, shared, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Table)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinct.Count < 2) continue;
            var parent = distinct.OrderBy(t => colCount.GetValueOrDefault(t, int.MaxValue)).First();
            foreach (var child in distinct)
            {
                if (string.Equals(child, parent, StringComparison.OrdinalIgnoreCase)) continue;
                Add(parent, shared, SerialColumn(cols, parent) ?? shared, child, shared);
            }
        }

        // 3) Relaciones de operadores (Resultados -> Usr/Admin por "No").
        AddCurated(Add, cols);

        return relations
            .OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RelationResultDto> RunAsync(string? connectionString, TableRelationDto relation, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(key))
            return new RelationResultDto(Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), 0, 0);

        var parent = await ReadTableAsync(connectionString,
            $"SELECT TOP 200 * FROM {QuoteTable(relation.ParentTable)} WHERE {QuoteIdent(relation.ParentSearchColumn)} = @k", key, ct);

        var childSql = $"SELECT TOP 1000 * FROM {QuoteTable(relation.ChildTable)} WHERE {QuoteIdent(relation.ChildKeyColumn)} IN " +
                       $"(SELECT {QuoteIdent(relation.ParentKeyColumn)} FROM {QuoteTable(relation.ParentTable)} WHERE {QuoteIdent(relation.ParentSearchColumn)} = @k)";
        var child = await ReadTableAsync(connectionString, childSql, key, ct);

        return new RelationResultDto(parent.Columns, parent.Rows, child.Columns, child.Rows, parent.Rows.Count, child.Rows.Count);
    }

    public Task<byte[]> ExportCsvAsync(TableRelationDto relation, RelationResultDto result, string key, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Csv(new[] { "Reporte", relation.Title }));
        sb.AppendLine(Csv(new[] { "Relación", $"{relation.ParentTable} ({relation.ParentKeyColumn}) -> {relation.ChildTable} ({relation.ChildKeyColumn})" }));
        sb.AppendLine(Csv(new[] { "Clave", key }));
        sb.AppendLine();

        sb.AppendLine(Csv(new[] { "Sección", "Tabla padre", relation.ParentTable }));
        sb.AppendLine(Csv(result.ParentColumns));
        foreach (var row in result.ParentRows) sb.AppendLine(Csv(row));
        sb.AppendLine();

        sb.AppendLine(Csv(new[] { "Sección", "Tabla hija", relation.ChildTable, $"Filas: {result.ChildRows.Count}" }));
        sb.AppendLine(Csv(result.ChildColumns));
        foreach (var row in result.ChildRows) sb.AppendLine(Csv(row));

        return Task.FromResult(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }

    public Task<byte[]> ExportPdfAsync(TableRelationDto relation, RelationResultDto result, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = LicenseType.Community;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(7).FontColor(Color.FromHex("#1D1D1F")));

                page.Header().Column(h =>
                {
                    h.Item().Row(row =>
                    {
                        row.RelativeItem().Text(relation.Title).FontSize(13).Bold().FontColor(Color.FromHex("#0071E3"));
                        row.ConstantItem(150).AlignRight().Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(7).FontColor(Color.FromHex("#6E6E73"));
                    });
                    h.Item().PaddingTop(2).Text($"Clave: {key}   ·   {relation.ParentTable} ({relation.ParentKeyColumn}) → {relation.ChildTable} ({relation.ChildKeyColumn})")
                        .FontSize(7).FontColor(Color.FromHex("#6E6E73"));
                    h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Color.FromHex("#E5E5EA"));
                });

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(4).Text($"Tabla padre — {relation.ParentTable} ({result.ParentRows.Count} filas)").FontSize(8).Bold();
                    col.Item().PaddingTop(2).Table(t => Table(t, result.ParentColumns, result.ParentRows));

                    col.Item().PaddingTop(10).Text($"Tabla hija — {relation.ChildTable} ({result.ChildRows.Count} filas)").FontSize(8).Bold();
                    col.Item().PaddingTop(2).Table(t => Table(t, result.ChildColumns, result.ChildRows));
                });

                page.Footer().AlignCenter().Text($"SXA-RTX Analytics · Reporte de relación · {key}").FontSize(6.5f).FontColor(Color.FromHex("#6E6E73"));
            });
        });

        return Task.FromResult(pdf.GeneratePdf());
    }

    // ------------------------------------------------------------------
    // Internos
    // ------------------------------------------------------------------

    private async Task<List<(string Table, string Column)>> LoadColumnsAsync(string cs, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        using var conn = SqlProbe.Create(cs);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' ORDER BY TABLE_NAME, ORDINAL_POSITION", conn)
        { CommandTimeout = 20 };
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add((r.GetString(0), r.GetString(1)));
        return result;
    }

    private static async Task<(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows)> ReadTableAsync(
        string cs, string sql, string key, CancellationToken ct)
    {
        var columns = new List<string>();
        var rows = new List<IReadOnlyList<string?>>();

        using var conn = SqlProbe.Create(cs);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        cmd.Parameters.Add(new SqlParameter("@k", key));
        using var r = await cmd.ExecuteReaderAsync(ct);
        columns.AddRange(Enumerable.Range(0, r.FieldCount).Select(r.GetName));
        while (await r.ReadAsync(ct))
        {
            var row = new string?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++)
                row[i] = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i)?.ToString();
            rows.Add(row);
        }
        return (columns, rows);
    }

    private static void Table(TableDescriptor table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var muted = Color.FromHex("#6E6E73");
        var surface = Color.FromHex("#F5F5F7");
        var border = Color.FromHex("#E5E5EA");

        table.ColumnsDefinition(def =>
        {
            foreach (var _ in columns) def.RelativeColumn();
        });

        table.Header(h =>
        {
            foreach (var col in columns)
                h.Cell().Background(surface).BorderBottom(1).BorderColor(border).Padding(3)
                    .Text(col).FontSize(6.5f).Bold().FontColor(muted);
        });

        foreach (var row in rows)
        {
            foreach (var cell in row)
                table.Cell().BorderBottom(0.5f).BorderColor(border).Padding(2.5f)
                    .Text(string.IsNullOrWhiteSpace(cell) ? "—" : Trunc(cell, 120)).FontSize(6.3f);
        }
    }

    private static void AddCurated(Action<string, string, string, string, string> add, List<(string Table, string Column)> cols)
    {
        var groups = new (string Result, string Usr, string Admin)[]
        {
            ("PackoutResultados", "PackoutUsr", "PackoutAdmin"),
            ("CabecerasResultados", "CabecerasUsr", "CabecerasAdmin"),
            ("CajasEResultados", "CajasEUsr", "CajasEAdmin"),
        };

        foreach (var g in groups)
        {
            if (!cols.Any(c => string.Equals(c.Table, g.Result, StringComparison.OrdinalIgnoreCase))) continue;
            add(g.Result, "Operador", "Serie", g.Usr, "No");
            add(g.Result, "OperadorAdmin", "Serie", g.Admin, "No");
        }
    }

    private static string? SerialColumn(List<(string Table, string Column)> cols, string table) =>
        cols.FirstOrDefault(c => string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase)
            && SerialHints.Any(s => c.Column.Contains(s, StringComparison.OrdinalIgnoreCase))).Column;

    private static bool IsId(string column) =>
        string.Equals(column, "ID", StringComparison.OrdinalIgnoreCase);

    private static string Category(string table)
    {
        if (table.Contains("VTi_", StringComparison.OrdinalIgnoreCase)) return "VTI";
        if (table.Contains("Packout", StringComparison.OrdinalIgnoreCase)) return "Packout";
        if (table.Contains("Cabecera", StringComparison.OrdinalIgnoreCase)) return "Cabeceras";
        if (table.Contains("Cajas", StringComparison.OrdinalIgnoreCase)) return "Cajas";
        if (table.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) return "Alarmas";
        if (table.Contains("nomen", StringComparison.OrdinalIgnoreCase) || table.Contains("_reports", StringComparison.OrdinalIgnoreCase)
            || table.Contains("inline", StringComparison.OrdinalIgnoreCase) || table.Contains("log_", StringComparison.OrdinalIgnoreCase)
            || table.Contains("txt_file", StringComparison.OrdinalIgnoreCase) || table.Contains("report", StringComparison.OrdinalIgnoreCase))
            return "Producción";
        return "General";
    }

    private static string Title(string parent, string child)
    {
        var p = Display(parent);
        var c = Display(child);
        if (p == "UutRecords" && c == "UutRecordDetails") return "Pruebas VTI (resultados por prueba)";
        if (p == "UutRecords" && c == "UutDefects") return "Defectos VTI por prueba";
        if (p == "Models" && c == "ModelParameters") return "Parámetros por modelo VTI";
        if (p == "Models" && c == "UutRecords") return "Pruebas VTI por modelo";
        if (p == "Alarms Config" && c == "Alarms Data") return "Datos de alarma por tag";
        if (p == "Alarms Config" && c == "Alarms Report") return "Reporte de alarmas por tag";
        if (p == "PackoutResultados" && c == "PackoutUsr") return "Operador de Packout";
        if (p == "PackoutResultados" && c == "PackoutAdmin") return "Administrador de Packout";
        if (p == "CabecerasResultados" && c == "CabecerasUsr") return "Operador de Cabeceras";
        if (p == "CabecerasResultados" && c == "CabecerasAdmin") return "Administrador de Cabeceras";
        if (p == "CajasEResultados" && c == "CajasEUsr") return "Operador de Cajas";
        if (p == "CajasEResultados" && c == "CajasEAdmin") return "Administrador de Cajas";
        return $"{p} → {c}";
    }

    private static string Display(string table) =>
        table.Replace("VTi_", "").Replace("VTech_", "").Replace("_reports", "").Replace("_", " ").Trim();

    private static string Normalize(string name)
    {
        var s = name;
        if (s.StartsWith("VTi_", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("VTech_", StringComparison.OrdinalIgnoreCase)) s = s[6..];
        if (s.EndsWith("_reports", StringComparison.OrdinalIgnoreCase)) s = s[..^8];
        if (s.EndsWith("es", StringComparison.OrdinalIgnoreCase) && s.Length > 3) s = s[..^2];
        else if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && s.Length > 2) s = s[..^1];
        return s.ToLowerInvariant();
    }

    private static string QuoteTable(string table) =>
        string.Join(".", table.Replace("[", "").Replace("]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => "[" + p + "]"));

    private static string QuoteIdent(string ident) => "[" + ident.Trim().Trim('[', ']') + "]";

    private static string Trunc(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static string Csv(IEnumerable<string?> values) =>
        string.Join(",", values.Select(CsvCell));

    private static string CsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
