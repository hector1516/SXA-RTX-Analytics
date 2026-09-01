# REPORTING — SXA-RTX Analytics

## Concepto
```
DataSource
    ↓
Query          — qué datos obtenemos (SQL + parámetros)
    ↓
QueryResult    — Columns + Rows + metadata
    ↓
Report         — cómo presentamos (renombrado, formato, visibilidad)
    ↓
Visualization  — cómo representamos gráficamente (barras, líneas, pastel, área, KPI)
    ↓
Dashboard      — composición de múltiples visualizaciones/reportes
```

Separación intencional: **Query ≠ Report ≠ Visualization ≠ Dashboard**.

## Reporting Engine

### Abstracción
```csharp
public interface IDataSourceProvider
{
    string ProviderName { get; }
    Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct);
}

public sealed record QueryRequest(
    Guid DataSourceId,
    string Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    int? MaxRows,
    TimeSpan? Timeout);

public sealed record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    TimeSpan ExecutionTime,
    bool IsTruncated);
```

- **SqlServerDataSourceProvider** — implementado (scaffold) con `Microsoft.Data.SqlClient`. Fase actual retorna vacío con timing/log; fase 2 abre conexión real, parametriza y mapea `SqlDataReader` → `QueryResult`.
- **OdbcDataSourceProvider** — pendiente (MAPICS). Misma interfaz, usa `System.Data.Odbc`.

El engine se registra con `AddReportingEngine()` y se consume desde Blazor, API, jobs y exportadores sin acoplar a UI.

### Fuentes de datos
- Tipo `DataSourceType` (`SqlServer`, `Odbc`). Cada `DataSource` guarda su cadena cifrada y estado activo/inactivo.
- No todas las fuentes soportan las mismas características; el provider abstrae diferencias (ej. paginación, tipos, timeout).

### Extensibilidad
- Nueva fuente = nueva clase `I*DataSourceProvider` + registro en DI; sin cambios en Domain/Application.
- Gráficas: solución Blazor intercambiable (barras, líneas, pastel/donut, área, KPI). Elegir librería madura solo cuando se necesite; no acoplar al dominio.

## Configuración sin recompilar
- `ApplicationSettings` (key-value) y entidades `DataSources/Reports/ReportColumns/ReportFilters/Charts/Dashboards` son editables desde UI/API en fases futuras.
- Renombrado de títulos, formatos y visibilidad se resuelve en capa `Report`/`ReportColumns` sin modificar `Query.Sql`.

## Seguridad de queries
- Siempre parametrizadas; nunca concatenar valores de usuario en SQL.
- `MaxRows` y `Timeout` obligatorios por defecto para evitar queries descontroladas.
- Conexiones de reporting con cuentas de solo lectura.

## Roadmap
- Fase 2: `DataSources` configurables, editor de `Query`, mapeo de `QueryResult`.
- Fase 3: `Reports/ReportColumns/ReportFilters`, export (CSV/Excel/PDF).
- Fase 4: `Charts` y `Dashboards`.
- Fase 5: Permisos por reporte/dashboard.
- Fase 6: ODBC/MAPICS.
