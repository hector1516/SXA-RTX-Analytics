# DEPLOYMENT — SXA-RTX Analytics (IIS)

> Fase 1 no incluye instalador. Este documento describe el despliegue manual esperado.

## Requisitos del servidor

- Windows Server 2019+ / Windows 10/11 para pruebas.
- **ASP.NET Core Hosting Bundle** correspondiente al TargetFramework (`net10.0` en scaffold, `net8.0` previsto). Incluye IIS Module (ANCM) y runtime.
  - Descarga: https://dotnet.microsoft.com/download
  - Tras instalar, `iisreset` o reiniciar.
- SQL Server accesible (para `SXA_RTX_Analytics`). Puede ser instancia local o remota.
- Cuenta de servicio / identidad de App Pool con acceso a la base y a la carpeta de la app.

## Publicación

```powershell
# Desde la raíz de la solución
dotnet publish src/SXA.RTX.Analytics.Web/SXA.RTX.Analytics.Web.csproj -c Release -o publish

# o con single-basket
dotnet publish SXA.RTX.Analytics.sln -c Release -o publish
```

Salida en `publish/` (no versionar). Contiene `web.config` generado para IIS.

## Crear sitio en IIS

1. **Application Pool**
   - .NET CLR Version: `No Managed Code`
   - Pipeline: Integrated
   - Identity: `ApplicationPoolIdentity` o cuenta de servicio dedicada
   - `Load User Profile: True` si se usa DPAPI/certificados

2. **Site**
   - Physical path: carpeta `publish/`
   - Binding: `https` con certificado válido (recomendado). `http` solo para pruebas internas.
   - HSTS ya configurado en `Program.cs` para no-Development.

3. **Permisos NTFS**
   - App Pool → Lectura/Ejecución en `publish/`
   - App Pool → Escritura en `logs/` (Serilog) si está bajo `publish/` o ruta externa configurada
   - Solo admins con escritura en `publish/`

4. **Configuración**
   - No editar `appsettings.json` publicado con secretos. Usar:
     - Variables de entorno del App Pool (`Configuration` → `Environment Variables`), o
     - `appsettings.Production.json` desplegado fuera del repo con ACL restringida, o
     - Registry / Key Vault según política ECCSA
   - Claves relevantes (doble guion bajo en variables de entorno):

     | Clave | Para qué |
     | --- | --- |
     | `ConnectionStrings__ConfigurationDatabase` | Base de configuración de la plataforma |
     | `ConnectionStrings__OperationalDatabase` | Base operacional que consulta `/query` (opcional) |
     | `ConnectionStrings__MapicsOdbc` | Cadena ODBC de MAPICS (opcional) |

   - Alternativa sin tocar el servidor: entrar como Administrador a `/configuration` y pulsar
     **Guardar conexiones**. Las cadenas se cifran con DPAPI en `SXA_RTX_ApplicationSettings`
     (atadas a la identidad del App Pool o del servicio). Precedencia completa en
     `docs/DATABASE.md`.
   - Después de cambiar la base operacional, recargar `/configuration` y confirmar que la cabecera
     de `/query` ya no dice "Base de configuración".

5. **Logs**
   - stdout: `logs/stdout` si se habilita en `web.config` (`stdoutLogEnabled="true"`)
   - App: `logs/sxa-rtx-analytics-*.log` (Serilog file sink, rolling diario, 14 días)
   - IIS: `%SystemDrive%\inetpub\logs\LogFiles\`

## Actualización

1. Descargar el instalador o el ZIP del release desde
   https://github.com/hector1516/SXA-RTX-Analytics/releases
2. `dotnet publish -c Release -o publish` (si se compila local)
3. Detener App Pool (o `app_offline.htm` en la raíz para graceful drain)
4. Copiar `publish/` al servidor (robocopy/xcopy)
5. Iniciar App Pool
6. Verificar `https://servidor/health` → `Healthy`

### Aviso de versión nueva

- El servidor consulta GitHub Releases una vez cada `UpdateCheck:IntervalMinutes` (60 por
  defecto, `0` desactiva) y guarda el resultado en memoria; los navegadores solo leen
  `/api/updates/check`, así que el coste no depende del número de usuarios.
- El administrador ve un modal con el changelog; el resto de usuarios lo ven una vez.
- **El aviso no instala nada**: el botón abre la página del release. La actualización real es
  instalar el `.exe` nuevo y reiniciar el App Pool.
- Intervalo configurable en caliente con la clave `App.UpdateCheckMinutes`.

## Health probes

- `GET /health` — JSON completo (útil para diagnóstico y balanceadores)
- `GET /health/live` — texto plano `Healthy`/`Unhealthy` (liveness probe ligera)

## Troubleshooting

| Síntoma | Causa probable |
|---------|----------------|
| 500.30 ANCM In-Process Start Failure | Hosting Bundle no instalado o versión distinta al TargetFramework |
| 500.31 ANCM Failed to Find Native Dependencies | Runtime no instalado |
| `configuration_database` Degraded | Cadena `ConfigurationDatabase` incorrecta o SQL Server inaccesible |
| Sin logs | App Pool sin permiso de escritura en `logs/` |

## Futuro instalador
Fase independiente: generará `publish/`, configurará IIS, creará base `SXA_RTX_Analytics` si no existe y guiará la configuración de cadenas.
