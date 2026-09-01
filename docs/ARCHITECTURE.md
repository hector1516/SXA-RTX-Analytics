# ARCHITECTURE — SXA-RTX Analytics

## Objetivo
Plataforma web interna de reporting: multi-fuente (SQL Server primario, ODBC/MAPICS secundario), reportes/graficas/dashboards configurables desde UI sin recompilar, con seguridad y auditoría.

## Estilo
- Modular, por capas, con separación de responsabilidades.
- Dominio independiente de infra y UI.
- Reporting Engine desacoplado de Blazor para reutilización desde API, jobs y exportadores.
- Sin sobreingeniería: cada abstracción resuelve una necesidad real o extensión claramente prevista.

## Diagrama de capas
```
┌─────────────────────────────────────────┐
│  Web (Blazor Web App)                   │  UI, navegación, auth futura, config
│  ├─ Components/Pages/Layout             │
│  └─ Program.cs / health / logging       │
├─────────────────────────────────────────┤
│  Application                            │  Casos de uso, DTOs, interfaces
├─────────────────────────────────────────┤
│  Domain                                 │  Entidades, VOs, enums, reglas
├─────────────────────────────────────────┤
│  Infrastructure                         │  EF Core, SQL Server, persistencia
├─────────────────────────────────────────┤
│  Reporting                              │  IDataSourceProvider, Query*, Providers
│                                         │  SqlServerDataSourceProvider (ahora)
│                                         │  OdbcDataSourceProvider (fase MAPICS)
└─────────────────────────────────────────┘
         │
         ▼
  SXA_RTX_Analytics (SQL Server)   ← base de configuración
  Bases operacionales externas     ← consultadas solo lectura vía Reporting Engine
```

## Dependencias (dirección)
```
Web → Application → Domain
Web → Infrastructure → (Application, Domain)
Web → Reporting → Domain
Infrastructure → Domain, Application
Reporting → Domain
Tests → su proyecto correspondiente
```
Nunca: Domain → Infra/Web. Nunca: Reporting → Blazor.

## Decisiones clave (Fase 1)

| Tema | Decisión | Razón |
|------|----------|-------|
| Target framework | `net10.0` en scaffold; previsto `net8.0 LTS` según spec | SDK disponible 10.0.303. Cambiar `<TargetFramework>` a `net8.0` es trivial cuando se instala SDK 8 |
| Blazor | `blazor` template, `interactivity None` (SSR) | Mínimo necesario en Fase 1; interactivo se activará donde haga falta |
| EF Core | 10.0.1 + SqlServer + InMemory fallback | Permite boot sin SQL Server en scaffold/CI; en prod se usa SqlServer real |
| Logging | Serilog AspNetCore + Console + File | Estructurado, editable sin código, con rolling |
| Health | `AddHealthChecks().AddDbContextCheck<ConfigurationDbContext>()` + `self` | `/health` JSON y `/health/live` texto |
| Reporting abstracción | `IDataSourceProvider.ExecuteAsync(QueryRequest) → QueryResult` | Aísla diferencias SQL Server/ODBC; cambiable sin tocar dominio |
| Gráficas/componentes | Ninguna en Fase 1 | Evitar dependencia prematura; arquitectura permite swap sin tocar dominio |
| Config DB | `SXA_RTX_Analytics` separada de operacionales | Seguridad y desacoplo |

## Estructura de solución
Ver `README.md`. Solución en `SXA.RTX.Analytics.sln` (formato `sln` clásico para compatibilidad).

## Flujo futuro de reporting
```
DataSource  → define conexión y tipo (SqlServer/Odbc)
Query       → "qué datos obtenemos" (SQL + parámetros)
QueryResult → Columns + Rows + metadata (renombrado/formato en Report)
Report      → "cómo presentamos" (mapeo de columnas, filtros, visibilidad)
Visualization → barras/líneas/pastel/área/KPI (intercambiable)
Dashboard   → composición de múltiples visualizaciones/reportes
```

## Configuración
- `appsettings.json` + `appsettings.Development.json` + User Secrets + env vars.
- Clave: `ConnectionStrings:ConfigurationDatabase`. Vacía → InMemory scaffold.

## Pruebas
- xUnit en `tests/*`. Fase 1: smoke tests de wiring y abstracciones.

## Despliegue
Ver `docs/DEPLOYMENT.md` — IIS + Hosting Bundle.

## Pendientes
- Elegir librería de gráficas (evaluar cuando toque: Blazor-Optics, ApexCharts.Blazor, ChartJs.Blazor — criterio: mantenida, sin acoplar dominio).
- Auth avanzada (Windows Auth / Entra ID según infraestructura ECCSA).
