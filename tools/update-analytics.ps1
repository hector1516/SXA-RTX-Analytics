<#
.SYNOPSIS
  Actualizador automatico de SXA-RTX Analytics.

.DESCRIPTION
  Consulta el ultimo release en GitHub, y si hay una version mas nueva que la
  instalada, descarga la publicacion, hace backup, la reemplaza y recicla la app.

  Se puede ejecutar manualmente o programarlo como tarea de Windows (el instalador
  lo registra con SCHTASKS cada hora). Funciona con la app como Windows Service o
  bajo IIS.

.PARAMETER InstallPath
  Carpeta donde esta instalada la app (p. ej. C:\Program Files\SXA-RTX Analytics).

.PARAMETER Mode
  "Service" (Windows Service, puerto 5000) o "IIS" (sitio con App Pool).

.PARAMETER ServiceName
  Nombre del servicio Windows (por defecto SXA-RTX-Analytics).

.PARAMETER AppPoolName
  Nombre del App Pool de IIS (por defecto SXA-RTX-Analytics).

.PARAMETER Repo
  Repositorio GitHub "owner/repo".

.PARAMETER DryRun
  Solo descarga y valida; no detiene ni reemplaza nada.

.PARAMETER KeepBackup
  Conserva la carpeta de backup tras una actualizacion correcta (por defecto se borra).
#>
param(
  [string]$InstallPath = "C:\Program Files\SXA-RTX Analytics",
  [ValidateSet("Service", "IIS")]
  [string]$Mode = "Service",
  [string]$ServiceName = "SXA-RTX-Analytics",
  [string]$AppPoolName = "SXA-RTX-Analytics",
  [string]$Repo = "hector1516/SXA-RTX-Analytics",
  [switch]$DryRun,
  [switch]$KeepBackup
)
$ErrorActionPreference = "Stop"

function Write-Step($m) { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $m" }

function Get-Normalized([string]$raw) {
  if ([string]::IsNullOrWhiteSpace($raw)) { return [version]"0.0.0" }
  $s = $raw.Trim().TrimStart('v', 'V')
  $dash = $s.IndexOfAny([char[]]@('-', '+'))
  if ($dash -ge 0) { $s = $s.Substring(0, $dash) }
  $parts = $s.Split('.') | Where-Object { $_ -ne '' }
  while ($parts.Count -lt 3) { $parts += "0" }
  $out = ($parts | Select-Object -First 3) -join '.'
  return [version]$out
}

try {
  $exe = Join-Path $InstallPath "SXA.RTX.Analytics.Web.exe"
  if (-not (Test-Path $exe)) { throw "No se encontro $exe" }
  $current = [version](Get-Item $exe).VersionInfo.FileVersion
  Write-Step "Version instalada: $current (modo $Mode)"

  $headers = @{ "User-Agent" = "SXA-RTX-Analytics-Updater"; "Accept" = "application/vnd.github+json" }
  $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers -TimeoutSec 30
  $tag = $latest.tag_name
  $latestVer = Get-Normalized $tag
  Write-Step "Ultimo release en GitHub: $tag"

  if ($latestVer -le $current) { Write-Step "Ya esta al dia. Nada que hacer."; exit 0 }

  $asset = $latest.assets | Where-Object { $_.name -like "*publish.zip" } | Select-Object -First 1
  if (-not $asset) { throw "No se encontro el asset publish.zip en $tag" }
  Write-Step "Descargando $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)..."

  $work = Join-Path $env:TEMP "sxa-rtx-update-$tag"
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $work | Out-Null
  $zip = Join-Path $work $asset.name
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $headers -TimeoutSec 300

  $stage = Join-Path $work "stage"
  Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
  if (-not (Test-Path (Join-Path $stage "SXA.RTX.Analytics.Web.exe"))) { throw "El zip no contiene SXA.RTX.Analytics.Web.exe" }
  Write-Step "Paquete validado en $stage"

  if ($DryRun) { Write-Step "DryRun: no se aplica. El paquete quedo en $stage"; exit 0 }

  $backup = "$InstallPath.backup.$tag.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

  if ($Mode -eq "Service") {
    Write-Step "Deteniendo servicio $ServiceName ..."
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 3
  } else {
    Write-Step "Reciclando App Pool $AppPoolName ..."
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
  }

  try {
    Write-Step "Reemplazando $InstallPath (backup en $backup)"
    Rename-Item -LiteralPath $InstallPath -NewName (Split-Path $backup -Leaf)
    Rename-Item -LiteralPath $stage -NewName (Split-Path $InstallPath -Leaf) -ErrorAction SilentlyContinue
    if (-not (Test-Path $InstallPath)) { Copy-Item -Recurse -Force (Join-Path $work (Split-Path $stage -Leaf)) $InstallPath }
  } catch {
    Write-Step "Fallo al reemplazar, restaurando backup..."
    if (Test-Path $InstallPath) { Remove-Item -Recurse -Force $InstallPath -ErrorAction SilentlyContinue }
    Rename-Item -LiteralPath $backup -NewName (Split-Path $InstallPath -Leaf)
    throw
  }

  if ($Mode -eq "Service") {
    & sc.exe start $ServiceName | Out-Null
    Write-Step "Servicio reiniciado."
  } else {
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Write-Step "App Pool reiniciado."
  }

  # Verificar salud
  Start-Sleep -Seconds 5
  try {
    $health = Invoke-WebRequest -Uri "http://localhost:5000/health/live" -UseBasicParsing -TimeoutSec 15
    if ($health.Content -match "Healthy") {
      Write-Step "OK: /health/live responde Healthy"
      if (-not $KeepBackup) { Remove-Item -Recurse -Force $backup -ErrorAction SilentlyContinue; Write-Step "Backup eliminado" }
      else { Write-Step "Backup conservado en $backup" }
    } else {
      throw "Health no saludable: $($health.Content)"
    }
  } catch {
    Write-Step "ADVERTENCIA: no se pudo verificar /health/live. Revisa manualmente. Backup conservado en $backup"
    if ($Mode -eq "Service") { & sc.exe start $ServiceName | Out-Null }
  }

  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
  Write-Step "Actualizacion completada a $tag"
  exit 0
}
catch {
  Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}