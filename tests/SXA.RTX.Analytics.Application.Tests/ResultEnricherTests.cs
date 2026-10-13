using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Application.Tests;

public class ResultEnricherTests
{
    private static QueryResult ResultWithOrigin() => new()
    {
        Columns = new List<QueryColumn>
        {
            new("Id", "Id", typeof(int)),
            new("OrigenPC", "OrigenPC", typeof(string)),
            new("Valor", "Valor", typeof(double)),
        },
        Rows = new List<IReadOnlyList<object?>>
        {
            new List<object?> { 1, "PC-AAA", 10.5 },
            new List<object?> { 2, "PC-BBB", 20.0 },
            new List<object?> { 3, "PC-DESCONOCIDO", 30.0 },
        },
        ExecutionTime = TimeSpan.FromMilliseconds(5),
    };

    private static IReadOnlyList<CatalogDeviceDto> Catalog() => new List<CatalogDeviceDto>
    {
        new("PC-AAA", "Celda 1", "VTI", "VTI-SmartStation", DateTime.UtcNow),
        new("PC-BBB", "Charger 2", "VTech", "VTech-R290", DateTime.UtcNow),
    };

    [Theory]
    [InlineData(null, "OrigenPC")]
    [InlineData("", "OrigenPC")]
    [InlineData("   ", "OrigenPC")]
    [InlineData("OrigenPC", "OrigenPC")]
    [InlineData("Nombre_PC", "Nombre_PC")]
    [InlineData("Mi]Col[umna", "MiColumna")]   // se neutralizan los corchetes
    public void SanitizeIdentifier_normaliza(string? entrada, string esperado)
    {
        Assert.Equal(esperado, ResultEnricher.SanitizeIdentifier(entrada));
    }

    [Fact]
    public void Enrich_agrega_nombre_y_tipo_por_fila()
    {
        var result = ResultEnricher.Enrich(ResultWithOrigin(), Catalog(), "OrigenPC");

        Assert.Equal(5, result.Columns.Count);
        Assert.Equal("NombrePC", result.Columns[3].Name);
        Assert.Equal("TipoMaquina", result.Columns[4].Name);

        Assert.Equal("Celda 1", result.Rows[0][3]);
        Assert.Equal("VTI", result.Rows[0][4]);
        Assert.Equal("Charger 2", result.Rows[1][3]);
        Assert.Equal("VTech", result.Rows[1][4]);
        Assert.Null(result.Rows[2][3]);          // sin coincidencia en el catálogo
    }

    [Fact]
    public void Enrich_usa_el_nombre_de_columna_configurado()
    {
        var result = new QueryResult
        {
            Columns = new List<QueryColumn> { new("Maquina", "Maquina", typeof(string)) },
            Rows = new List<IReadOnlyList<object?>> { new List<object?> { "PC-AAA" } },
        };

        var enriched = ResultEnricher.Enrich(result, Catalog(), "Maquina");

        Assert.Equal("Celda 1", enriched.Rows[0][1]);
    }

    [Fact]
    public void Enrich_no_cambia_el_resultado_si_no_esta_la_columna_de_origen()
    {
        var result = new QueryResult
        {
            Columns = new List<QueryColumn> { new("Valor", "Valor", typeof(double)) },
            Rows = new List<IReadOnlyList<object?>> { new List<object?> { 1.0 } },
        };

        var enriched = ResultEnricher.Enrich(result, Catalog(), "OrigenPC");

        Assert.Same(result, enriched);
    }

    [Fact]
    public void Enrich_sin_catalogo_devuelve_el_original()
    {
        var enriched = ResultEnricher.Enrich(ResultWithOrigin(), Array.Empty<CatalogDeviceDto>(), "OrigenPC");

        Assert.Equal(3, enriched.Columns.Count);
    }
}