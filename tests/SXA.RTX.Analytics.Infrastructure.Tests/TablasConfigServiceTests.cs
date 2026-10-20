using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Services;

namespace SXA.RTX.Analytics.Infrastructure.Tests;

public class TablasConfigServiceTests : InfrastructureTestBase
{
    [Fact]
    public async Task Guarda_fuente_titulo_serie_fecha_y_nicknames()
    {
        await using var db = NewDb(nameof(Guarda_fuente_titulo_serie_fecha_y_nicknames));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        var (ok, msg) = await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("[dbo].[report]", "Producción", "Reporte de proceso", "serialnum", "date", new List<TablaColumnaDto>
            {
                new("serialnum", "Serie"),
                new("temp_mp", "Temperatura MP"),
                new("date", null),
            }),
        });
        Assert.True(ok, msg);

        var item = Assert.Single(await sut.GetConfigAsync());
        Assert.Equal("Producción", item.Fuente);
        Assert.Equal("Reporte de proceso", item.DisplayName);
        Assert.Equal("serialnum", item.SerialColumn);
        Assert.Equal("date", item.DateColumn);
        Assert.Equal(3, item.Columnas.Count);
        Assert.Equal("Serie", item.Columnas.Single(c => c.ColumnName == "serialnum").DisplayName);
        Assert.Equal("Temperatura MP", item.Columnas.Single(c => c.ColumnName == "temp_mp").DisplayName);
        Assert.Null(item.Columnas.Single(c => c.ColumnName == "date").DisplayName);
    }

    [Fact]
    public async Task Sin_fuente_se_guarda_como_sin_clasificar()
    {
        await using var db = NewDb(nameof(Sin_fuente_se_guarda_como_sin_clasificar));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("[dbo].[Registros]", "", null, null, null, new List<TablaColumnaDto>()),
        });

        var item = (await sut.GetConfigAsync()).Single();
        Assert.Equal("Sin clasificar", item.Fuente);
        Assert.Empty(item.Columnas);
        Assert.Null(item.DisplayName);
    }

    [Fact]
    public async Task Guardar_de_nuevo_reemplaza_columnas_y_quita_tablas_eliminadas()
    {
        await using var db = NewDb(nameof(Guardar_de_nuevo_reemplaza_columnas_y_quita_tablas_eliminadas));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("T1", "VTI", "Uno", null, null, new List<TablaColumnaDto> { new("A", null), new("B", null) }),
            new("T2", "Packout", "Dos", null, null, new List<TablaColumnaDto> { new("C", null) }),
        });
        var (ok2, msg2) = await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("T1", "VTI", "Uno renombrado", "A", "B", new List<TablaColumnaDto> { new("A", "Alfa") }),
        });
        Assert.True(ok2, msg2);

        var item = Assert.Single(await sut.GetConfigAsync());
        Assert.Equal("T1", item.TableName);
        Assert.Equal("Uno renombrado", item.DisplayName);
        Assert.Equal("A", item.SerialColumn);
        Assert.Equal("Alfa", item.Columnas.Single().DisplayName);
        Assert.Empty(await db.Set<TablaColumna>().Where(c => c.ColumnName == "B").ToListAsync());
    }

    [Fact]
    public async Task El_import_de_tablas_trae_fuente_serie_fecha_y_nicknames()
    {
        await using var db = NewDb(nameof(El_import_de_tablas_trae_fuente_serie_fecha_y_nicknames));
        var export = new ConfigExportService(db, NullLogger<ConfigExportService>.Instance);
        var json = """
        {
          "TablasConfig": [
            {
              "TableName": "[dbo].[report]",
              "Fuente": "Producción",
              "DisplayName": "Reporte",
              "SerialColumn": "serialnum",
              "DateColumn": "date",
              "Columnas": [
                { "ColumnName": "serialnum", "DisplayName": "Serie" },
                { "ColumnName": "temp_mp", "DisplayName": "Temperatura" }
              ]
            }
          ]
        }
        """;

        var (ok, msg) = await export.ImportJsonAsync(json);

        Assert.True(ok, msg);
        var t = Assert.Single(await db.Set<TablaConfig>().Include(x => x.Columnas).ToListAsync());
        Assert.Equal("Producción", t.Fuente);
        Assert.Equal("serialnum", t.SerialColumn);
        Assert.Equal("date", t.DateColumn);
        Assert.Equal("Serie", t.Columnas.Single(c => c.ColumnName == "serialnum").DisplayName);
    }

    [Fact]
    public async Task El_import_acepta_columnas_como_texto_plano_legado()
    {
        await using var db = NewDb(nameof(El_import_acepta_columnas_como_texto_plano_legado));
        var export = new ConfigExportService(db, NullLogger<ConfigExportService>.Instance);
        var json = """
        { "TablasConfig": [ { "TableName": "T1", "Columnas": ["Id", "Valor"] } ] }
        """;

        var (ok, msg) = await export.ImportJsonAsync(json);

        Assert.True(ok, msg);
        var t = Assert.Single(await db.Set<TablaConfig>().Include(x => x.Columnas).ToListAsync());
        Assert.Equal("Sin clasificar", t.Fuente);
        Assert.Equal(new[] { "Id", "Valor" }, t.Columnas.Select(c => c.ColumnName).OrderBy(x => x).ToArray());
    }
}