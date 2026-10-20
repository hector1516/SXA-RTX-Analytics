# DEPLOYMENT - SXA-RTX Analytics (IIS / Servicio)


> El instalador (`Setup_SXA_RTX_Analytics_*.exe`) automatiza requisitos, IIS o servicio,
> permisos, firewall, configuración inicial y verificación de salud. Las secciones manuales
> quedan como referencia y diagnóstico.

## Requisitos del servidor

- Windows Server 2019+ / Windows 10/11 para pruebas.
- **ASP.NET Core Hosting Bundle** correspondiente al TargetFramework (`net10.0` en scaffold, `net8.0` previsto). Incluye IIS Module (ANCM) y runtime.
  - Descarga: https://dotnet.microsoft.com/download
  - Tras instalar, `iisreset` o reiniciar.
- SQL Server accesible (para `SXA_RTX_Analytics`). Puede ser instancia local o remota.
- Cuenta de servicio / identidad de App Pool con acceso a la base y a la carpeta de la app.

## Instalación automática (recomendada)

1. Descargar `Setup_SXA_RTX_Analytics_vX.Y.Z.exe` del release y ejecutarlo **como Administrador**.
2. El instalador:
   - Detecta IIS. Si existe: instala features (`Web-Server`, `Web-WebSockets`), crea el App Pool
     (No Managed Code, AlwaysRunning, Load User Profile) y el sitio en `http://localhost:5000`.
     Si no hay IIS: registra el servicio de Windows con reinicio ante fallos.
   - Instala el **ASP.NET Core Hosting Bundle 10** si falta (winget) y ejecuta `iisreset` si aplica.
   - Crea `data` y `logs` y ajusta permisos del App Pool: lectura/ejecución en la app y
     **Modify solo en `data` y `logs`**.
   - Abre el puerto en el firewall.
   - Inicializa la configuración (`--ensure-db`) y registra la tarea de actualización (cada hora).
   - Verifica `/health/live` y avisa si no responde `Healthy`.
3. Entrar a `http://servidor:5000` con `ECCSA` / `Qwe123456` y cambiarla (el instalador muestra
   estas credenciales al terminar y las deja en `PRIMER-INGRESO.txt`).
4. La conexión SQL queda **precargada** desde `appsettings.json` (se copia a 
   `appsettings.Production.json`). Puedes verla/cambiarla en **Configuración → SQL Server**.

Opciones del script (`installer\install.ps1`):

| Parámetro | Uso |
| --- | --- |
| `-Port 8080` | Otro puerto (también se usa en la tarea de actualización). |
| `-DryRun` | Muestra todo sin cambiar nada. |
| `-SkipPrereqs` | No intenta instalar features ni el Hosting Bundle (servidor sin internet). |
| `-ForceIis` | Fuerza modo IIS si la detección falla. |

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
- Intervalo configurable en caliente con la clave `App.UpdateCheckMinutes`.

### Actualización automática

El instalador registra una tarea de Windows (`SXA-RTX-Analytics-Updater`) que ejecuta
`tools\update-analytics.ps1` **cada hora** y hace, en orden:

1. Compara la versión instalada con el último release de GitHub.
2. Si hay versión nueva, descarga `SXA-RTX-Analytics-v*-publish.zip`.
3. Extrae a una carpeta temporal y valida que contenga el ejecutable.
4. Hace backup de la carpeta actual, detiene el servicio / recicla el App Pool, reemplaza los
   archivos y reinicia.
5. Verifica `http://localhost:${Port}/health/live` → `Healthy`. Si falla, conserva el backup.
6. **Preserva `data` (configuración SQLite) y `appsettings.Production.json`** y restaura los permisos del App Pool tras reemplazar la carpeta.

Se puede forzar en cualquier momento:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Program Files\SXA-RTX Analytics\tools\update-analytics.ps1" `
  -InstallPath "C:\Program Files\SXA-RTX Analytics" -Mode IIS    # o -Mode Service
```

> Nota: el reemplazo de archivos lo hace la tarea con permisos de SYSTEM. Si la app corre bajo
> IIS con una identidad restringida, la tarea sigue funcionando porque detiene el App Pool
> antes de copiar. Valida el flujo una vez en el servidor antes de dejarlo programado.

## Recuperación de acceso (superadmin)

Si **todos** olvidan su contraseña, se recupera sin tocar la base:

1. Define en el entorno del App Pool / servicio (o en `appsettings.Production.json`):

   | Clave | Valor |
   | --- | --- |
   | `BootstrapAdmin__Username` | `ECCSA` (o el que usen) |
   | `BootstrapAdmin__Password` | una contraseña temporal segura |
   | `BootstrapAdmin__ResetPassword` | `true` |

2. Reinicia la aplicación (recicla el App Pool o reinicia el servicio).
3. Entra con ese usuario y contraseña: la app te fuerza a cambiarla antes de continuar.
4. Retira `BootstrapAdmin__ResetPassword` (o borra la contraseña) para no volver a resetear.

En entornos locales/demo (base InMemory) se crea `ECCSA / Qwe123456` automáticamente. En
producción con SQL Server real, si no hay administrador activo y no se configura
`BootstrapAdmin__Password`, la app arranca sin administrador y lo avisa en el log.

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
