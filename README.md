# SXA-RTX Analytics

Plataforma web empresarial de reporting y análisis para ECCSA Automation — inspirada conceptualmente en Metabase, pero diseñada para el entorno interno de ECCSA.

> **Fase 1 — Bootstrap:** scaffold funcional, arquitectura base, logging, health checks y conexión inicial a la base de configuración. Sin constructor visual, dashboards, gráficas avanzadas, MAPICS ni instalador (fases posteriores).

## Stack
- .NET / ASP.NET Core / Blazor Web App (scaffold en `net10.0`; previsto `net8.0 LTS` — cambiar `TargetFramework` cuando el SDK 8 esté disponible)
- Entity Framework Core + SQL Server (`Microsoft.Data.SqlClient`) + ODBC (futuro MAPICS)
- Serilog (structured logging), Health Checks, InMemory fallback para scaffold sin SQL Server

## Estructura
```
SXA-RTX-Analytics/
├── SXA.RTX.Analytics.sln
├── src/
│   ├── SXA.RTX.Analytics.Web/           # Blazor UI, navegación, config, health
│   ├── SXA.RTX.Analytics.Application/   # Casos de uso, DTOs, interfaces
│   ├── SXA.RTX.Analytics.Domain/        # Entidades, enums, reglas
│   ├── SXA.RTX.Analytics.Infrastructure/# EF Core, SQL Server, persistencia
│   └── SXA.RTX.Analytics.Reporting/     # Reporting Engine (IDataSourceProvider)
├── tests/
│   ├── SXA.RTX.Analytics.Domain.Tests/
│   ├── SXA.RTX.Analytics.Application.Tests/
│   └── SXA.RTX.Analytics.Reporting.Tests/
├── database/{migrations,scripts}/
├── docs/{ARCHITECTURE,DATABASE,SECURITY,REPORTING,DEPLOYMENT}.md
├── installer/  (fase futura)
└── AGENTS.md
```

## Inicio rápido (local)

```powershell
# 1. Restaurar y compilar
dotnet restore SXA.RTX.Analytics.sln
dotnet build SXA.RTX.Analytics.sln -c Release

# 2. Tests
dotnet test SXA.RTX.Analytics.sln

# 3. Configurar cadena de configuración (opcional en Fase 1 — vacío usa InMemory)
dotnet user-secrets init --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj
dotnet user-secrets set "ConnectionStrings:ConfigurationDatabase" "Server=localhost;Database=SXA_RTX_Analytics;Trusted_Connection=True;TrustServerCertificate=True;" --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj

# 4. Ejecutar
dotnet run --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj
# Abrir http://localhost:5149  —  health en http://localhost:5149/health
```

## Configuración
- `ConnectionStrings:ConfigurationDatabase` — base `SXA_RTX_Analytics` (solo configuración, no datos operacionales). Nunca commitear el valor real; usar User Secrets o variables de entorno.
- Ver `docs/SECURITY.md` y `docs/DEPLOYMENT.md` para IIS y producción.

## Documentación
- `docs/ARCHITECTURE.md` — decisiones y diagrama de capas
- `docs/DATABASE.md` — modelo actual y futuro, estrategia de migraciones
- `docs/SECURITY.md` — secretos, cuentas de solo lectura, hardening
- `docs/REPORTING.md` — `DataSource → Query → QueryResult → Report → Visualization → Dashboard`
- `docs/DEPLOYMENT.md` — IIS, Hosting Bundle, publicación y actualización
- `AGENTS.md` — reglas para agentes/desarrolladores

## Health & Logging
- `GET /health` — JSON con estado de `configuration_database` y `self`
- `GET /health/live` — liveness probe (texto plano)
- Logs: consola + `logs/sxa-rtx-analytics-*.log` (Serilog, nunca incluye secretos)

## Roadmap de fases
1. **Bootstrap** (actual) — scaffold, arquitectura, logging, health, EF Core
2. Conexión a fuentes configurables, consultas, columnas/filtros
3. Reportes, gráficas y export
4. Dashboards
5. Roles/permisos/auditoría completa
6. ODBC/MAPICS
7. Instalador / IIS hardening

## Licencia
Uso interno ECCSA Automation.
