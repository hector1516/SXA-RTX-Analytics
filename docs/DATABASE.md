# DATABASE — SXA-RTX Analytics

## Bases
- `SXA_RTX_Analytics` - **configuración** propia de la plataforma. Creada y migrada por la app.
- Base **operacional** (SQL Server de negocio con las tablas VTI/VTech) - **solo lectura**, nunca migrada por esta app. Se configura aparte en `/configuration` o con `ConnectionStrings__OperationalDatabase`.
- `dbo.SXA_PCs` - catálogo de equipos del central de `SXA-RTX-Sync` (NombrePC, TipoMaquina, UltimoContacto). Solo lectura.
- MAPICS vía ODBC - **solo lectura**.

Separación estricta: nunca mezclar configuración con datos operacionales.

## Conexiones y precedencia

Cada conexión se resuelve con esta precedencia (primera que exista gana):

1. Valor guardado en `SXA_RTX_ApplicationSettings` (editado en `/configuration` → *Guardar conexiones*).
2. `appsettings.json` (`ConnectionStrings:*`).
3. Variable de entorno del App Pool / servicio: `ConnectionStrings__<Nombre>`.

| Clave guardada | Origen de appsettings | Variable de entorno | Para qué |
| --- | --- | --- | --- |
| `Sql.ConnectionString.Configuration` | `ConfigurationDatabase` | `ConnectionStrings__ConfigurationDatabase` | Base de configuración (este catálogo) |
| `Sql.ConnectionString.Operational` | `OperationalDatabase` | `ConnectionStrings__OperationalDatabase` | Base que consulta `/query` |
| `Odbc.ConnectionString.Mapics` | `MapicsOdbc` | `ConnectionStrings__MapicsOdbc` | MAPICS |

Si no hay base operacional configurada, `/query` consulta la base de configuración y la cabecera
muestra la etiqueta "Base de configuración".

### Cifrado en reposo

Los valores de las claves `Sql.ConnectionString.*` y `Odbc.ConnectionString.*` se cifran con
**DPAPI** (`DataProtectionScope.LocalMachine`): solo la identidad que corre la app (App Pool o
servicio de Windows) puede descifrarlos. El resto de ajustes se guardan en claro para poder
mostrarlos y exportarlos.

El export JSON **nunca** incluye esos valores: el equipo destino los reconfigura en `/configuration`.

## Entidades implementadas

### SXA_RTX_ApplicationSettings
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | Guid |
| Key | nvarchar(200) UNIQUE | Clave de configuración |
| Value | nvarchar(4000) | Valor; si `IsEncrypted`, cifrado con DPAPI |
| Description | nvarchar(1000) | Opcional |
| Category | nvarchar(100) | Opcional |
| IsEncrypted | bit | true solo para claves de conexión |
| CreatedAtUtc / UpdatedAtUtc | datetime2 | Auditoría |

Claves con significado propio:

| Key | Descripción |
| --- | --- |
| `Sql.ConnectionString.Configuration` | Conexión de esta base |
| `Sql.ConnectionString.Operational` | Conexión de la base operacional |
| `Odbc.ConnectionString.Mapics` | Conexión ODBC de MAPICS |
| `Sql.OperationalSourceName` | Nombre visible del origen operacional en la UI |
| `App.UpdateCheckMinutes` | Minutos entre comprobaciones de actualizaciones (0 = solo al cargar) |

### SXA_RTX_AuditLogs
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

### SXA_RTX_Equipos
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | |
| DeviceId | nvarchar(64) UNIQUE | `PC-<16 hex>`, lo genera SXA-RTX-Sync |
| Nombre | nvarchar(150) | Etiqueta amigable, se asigna en Analytics |
| Area | nvarchar(100) | Área / zona |
| Tipo | int | 1 = VTI, 2 = VTech, 0 = desconocido. Copia de `SXA_PCs.TipoMaquina` |
| Descripcion | nvarchar(500) | Opcional |
| IsActive | bit | |

### SXA_RTX_TablasConfig
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | |
| TableName | nvarchar(260) UNIQUE | `schema.table` de la base operacional |
| Tipo | int | 1 = VTI, 2 = VTech |

### SXA_RTX_Users
| Col | Tipo | Notas |
|-----|------|-------|
| Id | uniqueidentifier PK | |
| Username | nvarchar(100) UNIQUE | |
| PasswordHash | nvarchar(500) | Nunca se exporta |
| DisplayName | nvarchar(200) | |
| Role | int | 1 = Administrador, 2 = Usuario |
| IsActive | bit | Un usuario importado se crea inactivo |

## Filtros de /query contra la base operacional

`Tipo` y `Área` se resuelven contra `SXA_RTX_Equipos` (base de configuración) y se envían a la
base operacional como `OrigenPC IN (...)` parametrizado. Así la base operacional **no necesita**
conocer las tablas de la plataforma. `Equipo` usa igualdad simple (`OrigenPC = @deviceId`).

## Tablas del sistema que puede crear la UI

La pestaña *Tablas sistema* de `/configuration` ofrece crear estas tablas si faltan:

| Tabla | Notas |
| --- | --- |
| `SXA_RTX_ApplicationSettings` | Key-value de configuración |
| `SXA_RTX_AuditLogs` | Auditoría |
| `SXA_RTX_Users` | Usuarios internos |
| `SXA_RTX_Roles` | Roles y permisos |
| `SXA_RTX_UserRoles` | Asignación usuario↔rol (M:N) |
| `SXA_RTX_DataSources` | Catálogo de fuentes con cadena cifrada (reservada) |

Los nombres coinciden con los que mapea EF Core, así que crear la tabla desde la UI y dejar que la
app la use son equivalentes.

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
