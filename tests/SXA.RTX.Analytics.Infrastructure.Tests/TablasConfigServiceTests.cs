using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Domain.Enums;
using SXA.RTX.Analytics.Infrastructure.Services;

namespace SXA.RTX.Analytics.Infrastructure.Tests;

public class TablasConfigServiceTests : InfrastructureTestBase
{
    [Fact]
    public async Task Guarda_titulo_tipo_y_columnas_y_los_recupera()
    {
        await using var db = NewDb(nameof(Guarda_titulo_tipo_y_columnas_y_los_recupera));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        var (ok, msg) = await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("[dbo].[VTi_SmartStation_Log]", EquipoTipo.VTI, "Registros VTi", new List<string> { "Id", "OrigenPC", "Fecha" }),
        });
        Assert.True(ok, msg);

        var cfg = await sut.GetConfigAsync();
        var item = Assert.Single(cfg);
        Assert.Equal(EquipoTipo.VTI, item.Tipo);
        Assert.Equal("Registros VTi", item.DisplayName);
        Assert.Equal(new[] { "Fecha", "Id", "OrigenPC" }, item.VisibleColumns.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Columnas_vacias_significan_mostrar_todas()
    {
        await using var db = NewDb(nameof(Columnas_vacias_significan_mostrar_todas));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("[dbo].[Registros]", EquipoTipo.VTech, null, new List<string>()),
        });

        var item = (await sut.GetConfigAsync()).Single();
        Assert.Empty(item.VisibleColumns);
        Assert.Null(item.DisplayName);
    }

    [Fact]
    public async Task Guardar_de_nuevo_reemplaza_columnas_y_quita_tablas_eliminadas()
    {
        await using var db = NewDb(nameof(Guardar_de_nuevo_reemplaza_columnas_y_quita_tablas_eliminadas));
        var sut = new TablasConfigService(db, NullLogger<TablasConfigService>.Instance);

        await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("T1", EquipoTipo.VTI, "Uno", new List<string> { "A", "B" }),
            new("T2", EquipoTipo.VTech, "Dos", new List<string> { "C" }),
        });
        var (ok2, msg2) = await sut.SaveAsync(new List<TablaConfigDto>
        {
            new("T1", EquipoTipo.VTI, "Uno renombrado", new List<string> { "A" }),
        });
        Assert.True(ok2, msg2);

        var cfg = await sut.GetConfigAsync();
        var item = Assert.Single(cfg);
        Assert.Equal("T1", item.TableName);
        Assert.Equal("Uno renombrado", item.DisplayName);
        Assert.Equal(new[] { "A" }, item.VisibleColumns);
        Assert.Empty(await db.Set<TablaColumna>().Where(c => c.ColumnName == "B").ToListAsync());
    }

    [Fact]
    public async Task El_import_de_tablas_trae_titulo_y_columnas()
    {
        await using var db = NewDb(nameof(El_import_de_tablas_trae_titulo_y_columnas));
        var export = new ConfigExportService(db, NullLogger<ConfigExportService>.Instance);
        var json = """
        {
          "TablasConfig": [
            { "TableName": "[dbo].[Registros]", "Tipo": 1, "DisplayName": "Registros", "Columnas": ["Id", "Valor"] }
          ]
        }
        """;

        var (ok, msg) = await export.ImportJsonAsync(json);

        Assert.True(ok, msg);
        var cfg = await db.Set<TablaConfig>().ToListAsync();
        var t = Assert.Single(cfg);
        Assert.Equal("Registros", t.DisplayName);
        Assert.Equal(new[] { "Id", "Valor" }, t.Columnas.Select(c => c.ColumnName).OrderBy(x => x).ToArray());
    }
}