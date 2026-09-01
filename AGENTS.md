# AGENTS — SXA-RTX Analytics

Reglas operativas para agentes y desarrolladores que trabajen en este repo.

## 1. Leer antes de modificar
1. Leer `docs/ARCHITECTURE.md`, `docs/DATABASE.md`, `docs/SECURITY.md`, `docs/REPORTING.md`, `docs/DEPLOYMENT.md`.
2. Revisar `SXA.RTX.Analytics.sln` y referencias entre proyectos.
3. Identificar dependencias afectadas.

## 2. Separación de responsabilidades
- `SXA.RTX.Analytics.Web` — solo UI Blazor, navegación, auth futura, endpoints. Sin lógica de negocio.
- `SXA.RTX.Analytics.Application` — casos de uso, DTOs, validaciones, interfaces. Sin dependencia de Blazor.
- `SXA.RTX.Analytics.Domain` — entidades, value objects, enums, reglas. Sin infra ni UI.
- `SXA.RTX.Analytics.Infrastructure` — EF Core, SQL Server, persistencia, config.
- `SXA.RTX.Analytics.Reporting` — Reporting Engine y abstracciones `IDataSourceProvider`. Sin acoplamiento a Blazor.

## 3. Reglas de desarrollo
- Cambio más pequeño posible; no mezclar áreas no relacionadas en un mismo commit.
- Ejecutar `dotnet build` y `dotnet test` antes de commit.
- Revisar `git diff` y actualizar docs si aplica.
- No sobreingenierizar: preferir simple / modular / testeable.
- Documentar decisiones en `docs/`.

## 4. Configuración y secretos
- Nunca commitear `ConnectionStrings` reales, passwords, tokens, `.env`, `appsettings.*.local.json`.
- Usar User Secrets (`dotnet user-secrets`) en desarrollo y variables de entorno en producción/IIS.
- Conexiones de reporting preferentemente con cuentas de solo lectura.

## 5. Base de datos
- `SXA_RTX_Analytics` es la base de configuración (no mezclar con bases operacionales).
- Entidades futuras documentadas en `docs/DATABASE.md`. En Fase 1 solo `ApplicationSettings` y `AuditLogs`.
- Migraciones en `database/migrations` o vía `dotnet ef` desde `Infrastructure`.

## 6. Reporting
- Mantener separación: `DataSource → Query → QueryResult → Report → Visualization → Dashboard`.
- `IDataSourceProvider.ExecuteAsync` abstrae diferencias entre SQL Server y ODBC.
- El engine debe poder usarse desde Blazor, API, jobs y exportadores sin cambios de dominio.

## 7. Logging y Health
- Logging estructurado con Serilog (console + file `logs/`). Nunca loguear passwords/tokens/connection strings.
- Health: `/health` (JSON completo) y `/health/live` (texto). Añadir checks de DB/fuentes como tags independientes.

## 8. IIS
- La app debe publicarse como `dotnet publish -c Release`.
- Requiere ASP.NET Core Hosting Bundle en el servidor. Ver `docs/DEPLOYMENT.md`.

## 9. Git
- Commits pequeños y descriptivos: `chore:`, `feat:`, `fix:`, `docs:`, `test:`.
- No commitear `bin/`, `obj/`, `logs/`, `artifacts/`.
- Push solo si `dotnet build` y `dotnet test` pasan.

## 10. Stack
- .NET 8 LTS (este scaffold usa net10.0; cambiar `<TargetFramework>` a `net8.0` si se instala SDK 8).
- ASP.NET Core Blazor Web App, EF Core, SQL Server, Microsoft.Data.SqlClient, System.Data.Odbc.
- Sin dependencias innecesarias; librerías de gráficas/componentes solo si están mantenidas y son realmente necesarias.
