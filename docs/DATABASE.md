# DATABASE — SXA-RTX Analytics

## Bases
- `SXA_RTX_Analytics` — **configuración** propia de la plataforma. Creada y migrada por la app.
- Bases operacionales (SQL Server de negocio, MAPICS vía ODBC) — **solo lectura**, nunca migradas por esta app.

Separación estricta: nunca mezclar configuración con datos operacionales.

## Fase 1 — entidades implementadas

### ApplicationSettings
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | Guid |
| Key | nvarchar(200) UNIQUE | Clave de configuración |
| Value | nvarchar(4000) | Valor (encriptar si IsEncrypted) |
| Description | nvarchar(1000) | Opcional |
| Category | nvarchar(100) | Opcional (General, Security, etc.) |
| IsEncrypted | bit | Si true, valor cifrado en reposo (fase futura) |
| CreatedAtUtc / UpdatedAtUtc | datetime2 | Auditoría |

### AuditLogs
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | |
| TimestampUtc | datetime2, index | |
| Action | nvarchar(100) | e.g. Create, Update, Delete, Login |
| EntityName | nvarchar(200) | |
| EntityId | nvarchar(200) | FK lógica, no constraint |
| PerformedBy | nvarchar(200) | Usuario |
| Details | nvarchar(4000) | JSON o texto |
| IpAddress | nvarchar(45) | IPv4/IPv6 |
| CreatedAtUtc | datetime2 | |

## Modelo futuro (no implementado en Fase 1)

```
Users
Roles
Permissions
UserRoles (M:N)
RolePermissions (M:N)

DataSources { Id, Name, Type(SqlServer/Odbc), ConnectionString (encrypted), IsActive }

Reports { Id, Name, DataSourceId FK, SqlDefinition, CreatedBy, ... }
ReportColumns { Id, ReportId FK, SourceName, DisplayName, Format, IsVisible, Order }
ReportFilters { Id, ReportId FK, Column, Operator, DefaultValue, IsRequired }

Charts { Id, ReportId FK, Type(bar/line/pie/area/kpi), ConfigJson }
Dashboards { Id, Name, LayoutJson }
DashboardWidgets { Id, DashboardId FK, ReportId FK, ChartId FK, PositionJson }

AuditLogs (ya existente, se ampliará)
ApplicationSettings (ya existente)
```

Todas las tablas: `Id uniqueidentifier PK`, `CreatedAtUtc`, `UpdatedAtUtc` donde aplique.

## Migraciones

### Fase 1 (scaffold)
- Si `ConnectionStrings:ConfigurationDatabase` está vacía → proveedor `InMemory` → `EnsureCreatedAsync()` al arrancar, sin migraciones físicas.
- Si hay cadena SQL Server real → `EnsureCreatedAsync()` crea esquema mínimo (útil para demo). En cuanto haya SQL Server disponible, generar migración formal:

```powershell
dotnet ef migrations add Initial --project src/SXA.RTX.Analytics.Infrastructure --startup-project src/SXA.RTX.Analytics.Web --output-dir Persistence/Migrations
dotnet ef database update --project src/SXA.RTX.Analytics.Infrastructure --startup-project src/SXA.RTX.Analytics.Web
```

Migraciones se almacenan en `src/SXA.RTX.Analytics.Infrastructure/Persistence/Migrations` y scripts ad-hoc en `database/scripts/`.

### Estrategia
- Code-first, `ApplyConfigurationsFromAssembly`.
- Una migración por cambio de esquema; scripts `database/scripts/` solo para seed o datos de referencia.
- Nunca versionar una cadena real ni un `.mdf/.ldf`.

## Conexiones
- Cadena `ConfigurationDatabase` — editar vía User Secrets en dev, variable de entorno o `appsettings.Production.json` (no versionado) en prod.
- Conexiones de reporting (futuras `DataSources`) — cuentas de solo lectura, con timeout y `MaxRows` por query.
