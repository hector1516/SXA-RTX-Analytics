# SECURITY — SXA-RTX Analytics

## Principios
- Nunca commitear secretos: connection strings reales, passwords, API keys, tokens, certificados privados, `.env`, `appsettings.*.local.json`.
- Configuración externa: User Secrets en desarrollo, variables de entorno / Azure Key Vault / DPAPI en producción.
- Conexiones de reporting con cuentas de **solo lectura** (principio de mínimo privilegio).
- Logging nunca incluye secretos (Serilog enmascara; no loguear `ConnectionStrings`).

## Secretos — dónde van

| Entorno | Mecanismo |
|---------|-----------|
| Desarrollo | `dotnet user-secrets` en `SXA.RTX.Analytics.Web` |
| Test/CI | Variables de entorno del runner |
| Producción IIS | Variables de entorno del App Pool o `appsettings.Production.json` fuera del repo + ACLs NTFS |

### Ejemplo (desarrollo)
```powershell
dotnet user-secrets init --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj
dotnet user-secrets set "ConnectionStrings:ConfigurationDatabase" "Server=SRVSQL01;Database=SXA_RTX_Analytics;User Id=svc_sxa_analytics;Password=...;Encrypt=True;TrustServerCertificate=False;" --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj
dotnet user-secrets list --project src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj
```

## Cuentas de base de datos
- `SXA_RTX_Analytics` (config) — cuenta con DDL/DML limitada a su propia base; no `sysadmin`.
- Bases operacionales — **cuenta de solo lectura** (`db_datareader`) específica por `DataSource`. Nunca usar `sa` ni cuentas administrativas para queries de reporting.
- ODBC/MAPICS — DSN con credencial de solo lectura; timeout explícito.

## IIS (resumen, ver DEPLOYMENT.md)
- App Pool con identidad dedicada (ej. `IIS AppPool\SXA-RTX-Analytics` o cuenta de servicio gestionada).
- Carpeta de app con ACL solo para App Pool + admins.
- `logs/` escribible por App Pool.
- HTTPS obligatorio en producción; HSTS activado.

## Auditoría
- `AuditLogs` registra acción, entidad, usuario, IP y timestamp UTC.
- Fase futura: `Users/Roles/Permissions` + autorización por reporte/dashboard/datasource.

## Checklist pre-producción
- [ ] Ningún secreto en Git (`git log -p --all | grep -i password` limpio)
- [ ] `ConnectionStrings` solo en User Secrets / env vars
- [ ] Cuenta de reporting es solo lectura
- [ ] HTTPS + HSTS
- [ ] `logs/` fuera de `wwwroot`
- [ ] Health `/health` protegido o interno si se requiere
