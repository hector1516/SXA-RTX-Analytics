using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SXA.RTX.Analytics.Application.Abstractions;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Reporte PDF detallado de trazabilidad: portada con Hussmann, leyenda
/// TRAZABILIDAD y número de serie, KPIs, resumen por fuente, cronología
/// completa y detalle de cada evento (campos + resultados de pruebas).
/// El logo de la app se dibuja como marca de agua tenue en todas las páginas.
/// </summary>
public sealed class TraceReportService : ITraceReportService
{
    private static readonly Color Ink = Color.FromHex("#1D1D1F");
    private static readonly Color Muted = Color.FromHex("#6E6E73");
    private static readonly Color Border = Color.FromHex("#E5E5EA");
    private static readonly Color Surface = Color.FromHex("#F5F5F7");
    private static readonly Color Accent = Color.FromHex("#0071E3");
    private static readonly Color Green = Color.FromHex("#1D7A46");
    private static readonly Color Red = Color.FromHex("#B42318");

    public Task<byte[]> GeneratePdfAsync(
        string serial,
        IReadOnlyList<TraceEventDto> events,
        IReadOnlyList<TablaConfigDto> configs,
        TraceReportAssets assets,
        string? generatedBy = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = LicenseType.Community;

        var now = DateTime.Now;
        var ordered = events
            .OrderBy(e => e.OccurredAt ?? DateTime.MaxValue)
            .ThenBy(e => e.Fuente, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var configByTable = configs
            .Where(c => !string.IsNullOrWhiteSpace(c.TableName))
            .GroupBy(c => c.TableName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var views = ordered.Select((e, i) => BuildView(e, i + 1, configByTable)).ToList();
        var sources = BuildSources(views);
        var first = views.Where(v => v.Event.OccurredAt.HasValue).Select(v => v.Event.OccurredAt).Min();
        var last = views.Where(v => v.Event.OccurredAt.HasValue).Select(v => v.Event.OccurredAt).Max();
        var tests = views.Sum(v => v.Tests.Count);
        var passed = views.Sum(v => v.Passed);
        var failed = views.Sum(v => v.Failed);

        var document = Document.Create(container =>
        {
            // ---------- Portada ----------
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(46);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9).FontColor(Ink));
                Watermark(page, assets.AppLogo);

                page.Content().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("SXA-RTX ANALYTICS").FontSize(8).Bold().LetterSpacing(0.18f).FontColor(Muted);
                        row.ConstantItem(160).AlignRight().Text(now.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f).FontColor(Muted);
                    });

                    col.Item().PaddingTop(110);

                    if (assets.HussmannLogo is { Length: > 0 })
                        col.Item().AlignCenter().Width(240).Image(assets.HussmannLogo);

                    col.Item().PaddingTop(30).AlignCenter()
                        .Text("TRAZABILIDAD").FontSize(11).Bold().LetterSpacing(0.42f).FontColor(Accent);

                    col.Item().PaddingTop(8).AlignCenter()
                        .Text(serial).FontSize(34).Bold().FontFamily("Consolas");

                    col.Item().PaddingTop(5).AlignCenter()
                        .Text("Reporte detallado de trazabilidad").FontSize(11).FontColor(Muted);

                    col.Item().PaddingTop(28).LineHorizontal(1).LineColor(Border);

                    col.Item().PaddingTop(24).Row(row =>
                    {
                        Kpi(row, "Eventos", views.Count.ToString());
                        Kpi(row, "Fuentes", sources.Count.ToString());
                        Kpi(row, "Inicio", first?.ToString("yyyy-MM-dd HH:mm") ?? "-", small: true);
                        Kpi(row, "Duración", DurationText(first, last), last: true);
                    });

                    col.Item().PaddingTop(18).AlignCenter()
                        .Text($"{tests} {Plural(tests, "prueba registrada", "pruebas registradas")} · {passed} {Plural(passed, "aprobada", "aprobadas")} · {failed} {Plural(failed, "fallida", "fallidas")}")
                        .FontSize(8).FontColor(failed > 0 ? Red : Green);

                    col.Item().PaddingTop(24).AlignCenter()
                        .Text($"Generado {now:yyyy-MM-dd HH:mm}" + (string.IsNullOrWhiteSpace(generatedBy) ? "" : $" por {generatedBy}"))
                        .FontSize(8).FontColor(Muted);

                    col.Item().Extend().AlignBottom().AlignCenter()
                        .Text("ECCSA Automation · SXA-RTX Analytics · Uso interno y confidencial")
                        .FontSize(7.5f).FontColor(Muted);
                });
            });

            // ---------- Contenido ----------
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8).FontColor(Ink));
                Watermark(page, assets.AppLogo);

                page.Header().Column(h =>
                {
                    h.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("TRAZABILIDAD").FontSize(7).Bold().LetterSpacing(0.2f).FontColor(Accent);
                            c.Item().Text(serial).FontSize(13).Bold().FontFamily("Consolas");
                        });
                        if (assets.HussmannLogo is { Length: > 0 })
                            row.ConstantItem(110).AlignRight().AlignMiddle().Image(assets.HussmannLogo);
                    });
                    h.Item().PaddingTop(5).LineHorizontal(1).LineColor(Border);
                });

                page.Content().Column(col =>
                {
                    col.Item().Text("Resumen por fuente").FontSize(10).Bold();
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.6f);
                            c.ConstantColumn(44);
                            c.ConstantColumn(44);
                            c.ConstantColumn(52);
                            c.ConstantColumn(44);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(1.2f);
                        });

                        table.Header(h =>
                        {
                            HeaderCell(h, "Fuente");
                            HeaderCell(h, "Eventos", right: true);
                            HeaderCell(h, "Pruebas", right: true);
                            HeaderCell(h, "Aprobadas", right: true);
                            HeaderCell(h, "Fallidas", right: true);
                            HeaderCell(h, "Primera");
                            HeaderCell(h, "Última");
                        });

                        foreach (var s in sources)
                        {
                            BodyCell(table, s.Source);
                            BodyCell(table, s.Events.ToString(), right: true);
                            BodyCell(table, s.Tests == 0 ? "-" : s.Tests.ToString(), right: true);
                            BodyCell(table, s.Tests == 0 ? "-" : s.Passed.ToString(), right: true);
                            BodyCell(table, s.Failed.ToString(), right: true, color: s.Failed > 0 ? Red : (Color?)null);
                            BodyCell(table, s.First?.ToString("yyyy-MM-dd HH:mm") ?? "-");
                            BodyCell(table, s.Last?.ToString("yyyy-MM-dd HH:mm") ?? "-");
                        }
                    });

                    col.Item().PaddingTop(16).Text("Cronología de eventos").FontSize(10).Bold();
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(24);
                            c.ConstantColumn(86);
                            c.ConstantColumn(54);
                            c.RelativeColumn(1.5f);
                            c.ConstantColumn(46);
                            c.RelativeColumn(2.4f);
                        });

                        table.Header(h =>
                        {
                            HeaderCell(h, "#", right: true);
                            HeaderCell(h, "Fecha y hora");
                            HeaderCell(h, "Fuente");
                            HeaderCell(h, "Tabla");
                            HeaderCell(h, "Pruebas", right: true);
                            HeaderCell(h, "Resumen");
                        });

                        foreach (var v in views)
                        {
                            BodyCell(table, v.Index.ToString(), right: true);
                            BodyCell(table, v.Event.OccurredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Sin fecha");
                            BodyCell(table, v.Event.Fuente ?? "Sin fuente");
                            BodyCell(table, v.Event.TableDisplayName);
                            BodyCell(table, v.Tests.Count == 0 ? "-" : v.Tests.Count.ToString(), right: true);
                            BodyCell(table, v.Summary, color: v.Failed > 0 ? Red : (Color?)null);
                        }
                    });

                    col.Item().PageBreak();

                    col.Item().Text("Detalle de eventos").FontSize(10).Bold();
                    col.Item().PaddingTop(2).Text("Se listan todos los campos del registro y, cuando existe, el detalle de las pruebas.")
                        .FontSize(7.5f).FontColor(Muted);

                    foreach (var v in views)
                        EventBlock(col, v);
                });

                page.Footer().Column(f =>
                {
                    f.Item().LineHorizontal(1).LineColor(Border);
                    f.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("SXA-RTX Analytics · ECCSA Automation").FontSize(6.8f).FontColor(Muted);
                        row.RelativeItem().AlignCenter().Text($"Trazabilidad · {serial}").FontSize(6.8f).FontColor(Muted);
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(6.8f).FontColor(Muted));
                            t.CurrentPageNumber();
                            t.Span(" / ");
                            t.TotalPages();
                        });
                    });
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    // ------------------------------------------------------------------
    // Modelo de vista
    // ------------------------------------------------------------------

    private sealed record TestRow(string Date, string Title, string Result, string Value, string Units, bool Failed);

    private sealed record EventView(
        TraceEventDto Event,
        int Index,
        IReadOnlyList<KeyValuePair<string, string?>> Fields,
        IReadOnlyList<TestRow> Tests,
        int Passed,
        int Failed,
        string Summary);

    private sealed record SourceRow(
        string Source, int Events, int Tests, int Passed, int Failed, DateTime? First, DateTime? Last);

    private static EventView BuildView(TraceEventDto e, int index, IReadOnlyDictionary<string, TablaConfigDto> configs)
    {
        configs.TryGetValue(e.TableName, out var cfg);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<KeyValuePair<string, string?>>();

        void Add(string column, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!seen.Add(column)) return;
            if (fields.Count >= 40) return;
            fields.Add(new KeyValuePair<string, string?>(Nick(cfg, column), Trim(value, 420)));
        }

        if (cfg is not null)
            foreach (var col in cfg.Columnas)
                if (e.Values.TryGetValue(col.ColumnName, out var value))
                    Add(col.ColumnName, value);

        foreach (var kv in e.Values)
            Add(kv.Key, kv.Value);

        var detail = cfg?.Detalle;
        if (detail is null && e.TableName.Contains("VTi_UutRecords", StringComparison.OrdinalIgnoreCase))
            detail = new TablaDetalleDto("dbo.VTi_UutRecordDetails", "UutRecordID", "ID", "DateTime", "Test", "Result", "Value", "Units");

        var tests = new List<TestRow>();
        if (e.Details is { Count: > 0 } && detail is not null)
        {
            foreach (var row in e.Details)
            {
                var result = Val(row, detail.ResultColumn);
                var failed = result.Contains("fail", StringComparison.OrdinalIgnoreCase)
                             || result.Contains("fall", StringComparison.OrdinalIgnoreCase);
                tests.Add(new TestRow(
                    Trim(Val(row, detail.DateColumn), 19),
                    Val(row, detail.TitleColumn),
                    result,
                    Val(row, detail.ValueColumn),
                    Val(row, detail.UnitsColumn),
                    failed));
            }
        }

        var passed = tests.Count(t => !t.Failed);
        var failedCount = tests.Count(t => t.Failed);

        var parts = fields.Take(3).Select(f => $"{f.Key}: {Trim(f.Value, 44)}").ToList();
        if (tests.Count > 0)
            parts.Add($"{tests.Count} {Plural(tests.Count, "prueba", "pruebas")} ({failedCount} {Plural(failedCount, "fallida", "fallidas")})");

        return new EventView(e, index, fields, tests, passed, failedCount, string.Join(" · ", parts));
    }

    private static List<SourceRow> BuildSources(IReadOnlyList<EventView> views) =>
        views
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Event.Fuente) ? "Sin fuente" : v.Event.Fuente!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SourceRow(
                g.Key,
                g.Count(),
                g.Sum(v => v.Tests.Count),
                g.Sum(v => v.Passed),
                g.Sum(v => v.Failed),
                g.Where(v => v.Event.OccurredAt.HasValue).Select(v => v.Event.OccurredAt).Min(),
                g.Where(v => v.Event.OccurredAt.HasValue).Select(v => v.Event.OccurredAt).Max()))
            .OrderByDescending(s => s.Events)
            .ThenBy(s => s.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ------------------------------------------------------------------
    // Render
    // ------------------------------------------------------------------

    private static void Watermark(PageDescriptor page, byte[]? logo)
    {
        if (logo is not { Length: > 0 }) return;
        page.Background().Layers(layers =>
        {
            layers.PrimaryLayer().AlignCenter().AlignMiddle().Width(360).Image(logo);
            layers.Layer().Extend().Background(Colors.White.WithAlpha(0.95f));
        });
    }

    private static void Kpi(RowDescriptor row, string label, string value, bool last = false, bool small = false)
    {
        var item = row.RelativeItem();
        if (!last) item = item.PaddingRight(8);
        item.Background(Surface).Border(1).BorderColor(Border).Padding(9).Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(6.8f).Bold().LetterSpacing(0.08f).FontColor(Muted);
            col.Item().PaddingTop(3).Text(value).FontSize(small ? 10.5f : 13).Bold();
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().Background(Surface).BorderBottom(1).BorderColor(Border).Padding(4);
        if (right) cell = cell.AlignRight();
        cell.Text(text).FontSize(6.8f).Bold().FontColor(Muted);
    }

    private static void BodyCell(TableDescriptor table, string text, bool right = false, Color? color = null)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Border).Padding(3.5f);
        if (right) cell = cell.AlignRight();
        var label = cell.Text(string.IsNullOrWhiteSpace(text) ? "—" : text).FontSize(7.2f);
        if (color is not null) label.FontColor(color.Value);
    }

    private static void EventBlock(ColumnDescriptor col, EventView v)
    {
        col.Item().PaddingTop(12).PreventPageBreak().Column(c =>
        {
            c.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span($"#{v.Index}").Bold().FontSize(9);
                    t.Span("  ·  ").FontColor(Muted);
                    t.Span(v.Event.OccurredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Sin fecha").Bold();
                });
                row.ConstantItem(190).AlignRight()
                    .Text($"{v.Event.Fuente} · {v.Event.TableDisplayName}")
                    .FontSize(7.2f).FontColor(Muted);
            });

            if (v.Tests.Count > 0)
                c.Item().PaddingTop(1).Text($"{v.Tests.Count} {Plural(v.Tests.Count, "prueba", "pruebas")} · {v.Passed} {Plural(v.Passed, "aprobada", "aprobadas")} · {v.Failed} {Plural(v.Failed, "fallida", "fallidas")}")
                    .FontSize(7.2f).FontColor(v.Failed > 0 ? Red : Green);

            if (v.Fields.Count > 0)
                c.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(84); cols.RelativeColumn();
                        cols.ConstantColumn(84); cols.RelativeColumn();
                    });

                    for (var i = 0; i < v.Fields.Count; i += 2)
                    {
                        var a = v.Fields[i];
                        var hasB = i + 1 < v.Fields.Count;
                        var b = hasB ? v.Fields[i + 1] : default;

                        table.Cell().PaddingVertical(1.5f).PaddingRight(6)
                            .Text(a.Key).FontSize(6.6f).Bold().FontColor(Muted);
                        table.Cell().PaddingVertical(1.5f).PaddingRight(12)
                            .Text(a.Value ?? "").FontSize(7.2f);
                        table.Cell().PaddingVertical(1.5f).PaddingRight(6)
                            .Text(hasB ? b.Key : "").FontSize(6.6f).Bold().FontColor(Muted);
                        table.Cell().PaddingVertical(1.5f)
                            .Text(hasB ? b.Value ?? "" : "").FontSize(7.2f);
                    }
                });
        });

        if (v.Tests.Count > 0)
            col.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(94);
                    c.RelativeColumn(2.2f);
                    c.ConstantColumn(54);
                    c.RelativeColumn(1.3f);
                    c.ConstantColumn(46);
                });

                table.Header(h =>
                {
                    HeaderCell(h, "Fecha");
                    HeaderCell(h, "Prueba");
                    HeaderCell(h, "Resultado");
                    HeaderCell(h, "Valor");
                    HeaderCell(h, "Unidades");
                });

                foreach (var t in v.Tests)
                {
                    var bg = t.Failed ? Red.WithAlpha(0.06f) : Colors.Transparent;
                    var resultColor = t.Failed ? Red : Green;
                    RowCell(table, t.Date, bg);
                    RowCell(table, t.Title, bg);
                    RowCell(table, t.Result, bg, resultColor);
                    RowCell(table, t.Value, bg);
                    RowCell(table, t.Units, bg);
                }
            });

        col.Item().PaddingTop(8).LineHorizontal(0.75f).LineColor(Border);
    }

    private static void RowCell(TableDescriptor table, string text, Color background, Color? color = null)
    {
        var cell = table.Cell().Background(background).BorderBottom(0.5f).BorderColor(Border).Padding(3);
        var label = cell.Text(string.IsNullOrWhiteSpace(text) ? "—" : Trim(text, 160)).FontSize(7);
        if (color is not null) label.FontColor(color.Value);
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    private static string Nick(TablaConfigDto? cfg, string column)
    {
        var col = cfg?.Columnas.FirstOrDefault(c => string.Equals(c.ColumnName, column, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(col?.DisplayName) ? column : col!.DisplayName!;
    }

    private static string Val(IReadOnlyDictionary<string, string?> row, string? column) =>
        string.IsNullOrWhiteSpace(column) ? "" : (row.TryGetValue(column!, out var value) ? value ?? "" : "");

    private static string DurationText(DateTime? first, DateTime? last)
    {
        if (first is null || last is null) return "-";
        var total = (long)(last.Value - first.Value).TotalSeconds;
        if (total < 0) total = 0;
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return $"{h}h {m:00}m {s:00}s";
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    private static string Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max] + "…");
}
