<#
.SYNOPSIS
  Instalador/desinstalador automático de SXA-RTX Analytics (IIS o Windows Service).

.DESCRIPTION
  - Detecta IIS; si existe crea App Pool + sitio y ajusta permisos/seguridad.
  - Si no hay IIS, registra la app como servicio de Windows.
  - Instala prerequisitos si faltan (features IIS y ASP.NET Core Hosting Bundle 10 vía winget).
  - Crea data/ y logs/, abre el puerto en el firewall, inicializa la configuración
    (--ensure-db) y registra la tarea de actualización automática (cada hora).
  - Verifica /health/live al final.

.PARAMETER InstallPath
  Carpeta de instalación. Por defecto C:\Program Files\SXA-RTX Analytics.

.PARAMETER Mode
  install | uninstall.

.PARAMETER Port
  Puerto HTTP. Por defecto 5000.

.PARAMETER DryRun
  Muestra lo que haría sin cambiar nada.

.PARAMETER SkipPrereqs
  No intenta instalar features ni el Hosting Bundle.

.PARAMETER ForceIis
  Fuerza el modo IIS (útil si la detección falla).
#>
param(
  [string]$InstallPath = "C:\Program Files\SXA-RTX Analytics",
  [ValidateSet("install","uninstall")]
  [string]$Mode = "install",
  [int]$Port = 5000,
  [switch]$DryRun,
  [switch]$SkipPrereqs,
  [switch]$ForceIis
)

$ErrorActionPreference = "Stop"

$SiteName     = "SXA-RTX-Analytics"
$AppPoolName  = "SXA-RTX-Analytics"
$ServiceName  = "SXA-RTX-Analytics"
$TaskName     = "SXA-RTX-Analytics-Updater"
$FirewallRule = "SXA-RTX Analytics"
$script:NeedIisReset = $false
$script:LogFile = Join-Path $env:TEMP "sxa-rtx-install.log"

function Write-Step([string]$msg) {
  $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
  Write-Host $line
  try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

function Test-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsIIS {
  if ($ForceIis) { return $true }
  if (Get-Service -Name W3SVC -ErrorAction SilentlyContinue) { return $true }
  try {
    $f = Get-WindowsFeature -Name Web-Server -ErrorAction Stop
    return [bool]($f -and $f.Installed)
  } catch { return $false }
}

function Invoke-Step([string]$desc, [scriptblock]$action) {
  Write-Step $desc
  if ($DryRun) { Write-Step "  (DryRun) omitido"; return }
  & $action
}

function Test-AspNetCoreRuntime {
  try {
    $out = & dotnet --list-runtimes 2>$null
    return [bool]($out -match 'Microsoft\.AspNetCore\.App 10\.')
  } catch { return $false }
}

function Ensure-AspNetCoreRuntime {
  if (Test-AspNetCoreRuntime) { Write-Step "ASP.NET Core 10 detectado."; return }
  Invoke-Step "Instalando ASP.NET Core Hosting Bundle 10 (winget)" {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
      throw "Falta ASP.NET Core Hosting Bundle 10 y winget no está disponible. Instálalo desde https://dotnet.microsoft.com/download/dotnet/10.0 y vuelve a ejecutar."
    }
    & winget install --id Microsoft.DotNet.HostingBundle.10 -e --silent --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity
    if (-not (Test-AspNetCoreRuntime)) {
      throw "No se pudo instalar el Hosting Bundle (winget código $LASTEXITCODE). Instálalo manualmente y reintenta."
    }
    $script:NeedIisReset = $true
  }
}

function Ensure-IISFeatures {
  if (-not (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue)) {
    Write-Step "Get-WindowsFeature no disponible; se asume IIS ya instalado."
    return
  }
  $need = @('Web-Server','Web-WebSockets') | Where-Object {
    $f = Get-WindowsFeature -Name $_ -ErrorAction SilentlyContinue
    -not ($f -and $f.Installed)
  }
  if (-not $need) { Write-Step "Features IIS OK (Web-Server, Web-WebSockets)."; return }
  Invoke-Step "Instalando features IIS: $($need -join ', ')" {
    Install-WindowsFeature -Name $need -IncludeManagementTools | Out-Null
    $script:NeedIisReset = $true
  }
}

function Ensure-Directories {
  Invoke-Step "Creando carpetas data y logs" {
    New-Item -ItemType Directory -Force -Path $InstallPath, (Join-Path $InstallPath 'data'), (Join-Path $InstallPath 'logs') | Out-Null
  }
}

function Grant-AppPoolAccess {
  $identity = "IIS AppPool\$AppPoolName"
  Invoke-Step "Permisos: lectura/ejecución en la app y Modify solo en data/logs ($identity)" {
    $acl = Get-Acl -LiteralPath $InstallPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
      $identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $InstallPath -AclObject $acl

    foreach ($sub in @('data', 'logs')) {
      $dir = Join-Path $InstallPath $sub
      New-Item -ItemType Directory -Force -Path $dir | Out-Null
      $sacl = Get-Acl -LiteralPath $dir
      $srule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $identity, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
      $sacl.SetAccessRule($srule)
      Set-Acl -LiteralPath $dir -AclObject $sacl
    }
  }
}

function Set-FirewallRule {
  Invoke-Step "Abriendo puerto TCP $Port en el firewall" {
    & netsh advfirewall firewall delete rule name="$FirewallRule" | Out-Null
    & netsh advfirewall firewall add rule name="$FirewallRule" dir=in action=allow protocol=TCP localport=$Port | Out-Null
  }
}

function Install-IIS {
  Invoke-Step "Configurando App Pool $AppPoolName (No Managed Code, AlwaysRunning, Load User Profile)" {
    Import-Module WebAdministration -ErrorAction Stop
    $pool = "IIS:\AppPools\$AppPoolName"
    if (-not (Test-Path $pool)) { New-WebAppPool -Name $AppPoolName | Out-Null }
    Set-ItemProperty $pool -Name managedRuntimeVersion -Value ""
    Set-ItemProperty $pool -Name managedPipelineMode -Value Integrated
    Set-ItemProperty $pool -Name processModel.identityType -Value ApplicationPoolIdentity
    Set-ItemProperty $pool -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty $pool -Name startMode -Value AlwaysRunning
    Set-ItemProperty $pool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $pool -Name enable32BitAppOnWin64 -Value $false
  }

  Invoke-Step "Creando sitio IIS $SiteName en http://localhost:$Port" {
    if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) { Remove-Website -Name $SiteName }
    New-Website -Name $SiteName -PhysicalPath $InstallPath -ApplicationPool $AppPoolName -Port $Port -Force | Out-Null
  }

  Grant-AppPoolAccess
  Set-FirewallRule
}

function Install-Service {
  $exe = Join-Path $InstallPath "SXA.RTX.Analytics.Web.exe"
  Invoke-Step "Registrando servicio Windows $ServiceName (automático, con reinicio ante fallo)" {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      & sc.exe delete $ServiceName | Out-Null
      Start-Sleep -Seconds 2
    }
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`" --urls http://0.0.0.0:$Port" `
      -DisplayName "SXA-RTX Analytics" -StartupType Automatic | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Start-Service -Name $ServiceName
  }
  Set-FirewallRule
}

function Invoke-EnsureDb {
  $exe = Join-Path $InstallPath "SXA.RTX.Analytics.Web.exe"
  if (-not (Test-Path $exe)) { Write-Step "No se encontró $exe; se omite --ensure-db."; return }
  Invoke-Step "Inicializando configuración local (--ensure-db)" {
    & $exe --ensure-db --urls "http://localhost:$Port" | Out-Null
  }
}

function Register-Updater([string]$runtimeMode) {
  $updater = Join-Path $InstallPath "tools\update-analytics.ps1"
  if (-not (Test-Path $updater)) { Write-Step "No se encontró tools\update-analytics.ps1; se omite la tarea programada."; return }
  $tr = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$updater`" -InstallPath `"$InstallPath`" -Mode $runtimeMode -Port $Port"
  Invoke-Step "Registrando tarea $TaskName (cada hora)" {
    & schtasks /Create /TN $TaskName /SC HOURLY /RU SYSTEM /RL HIGHEST /F /TR $tr | Out-Null
  }
}

function Wait-Healthy([int]$timeoutSeconds = 90) {
  if ($DryRun) { Write-Step "(DryRun) se omite la verificación de salud"; return $true }
  Write-Step "Verificando http://localhost:$Port/health/live ..."
  $deadline = (Get-Date).AddSeconds($timeoutSeconds)
  do {
    try {
      $r = Invoke-WebRequest -Uri "http://localhost:$Port/health/live" -UseBasicParsing -TimeoutSec 5
      if ($r.Content -match 'Healthy') { Write-Step "OK: /health/live responde Healthy"; return $true }
    } catch { }
    Start-Sleep -Seconds 3
  } while ((Get-Date) -lt $deadline)
  Write-Step "ADVERTENCIA: /health/live no respondió Healthy en $timeoutSeconds s."
  return $false
}

function Uninstall {
  Write-Step "Desinstalando SXA-RTX Analytics..."
  if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Invoke-Step "Deteniendo y eliminando el servicio $ServiceName" {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      & sc.exe delete $ServiceName | Out-Null
    }
  }
  if (Test-IsIIS) {
    Invoke-Step "Eliminando sitio y App Pool de IIS" {
      Import-Module WebAdministration -ErrorAction SilentlyContinue
      if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) { Remove-Website -Name $SiteName }
      if (Test-Path "IIS:\AppPools\$AppPoolName") { Remove-WebAppPool -Name $AppPoolName }
    }
  }
  Invoke-Step "Eliminando tarea programada" { & schtasks /Delete /TN $TaskName /F | Out-Null }
  Invoke-Step "Eliminando regla de firewall" { & netsh advfirewall firewall delete rule name="$FirewallRule" | Out-Null }
  Write-Step "Listo. Se conserva la configuración en $InstallPath\data."
}

try {
  Write-Step "SXA-RTX Analytics - modo=$Mode puerto=$Port dryRun=$DryRun"
  if (-not $DryRun -and -not (Test-Admin)) { throw "Ejecuta este script como Administrador." }

  if ($Mode -eq "uninstall") { Uninstall; exit 0 }

  $isIis = Test-IsIIS
  Write-Step ("Modo de ejecución detectado: " + $(if ($isIis) { "IIS" } else { "Windows Service" }))

  Ensure-Directories

  if (-not $SkipPrereqs) {
    if ($isIis) { Ensure-IISFeatures }
    Ensure-AspNetCoreRuntime
  } else {
    Write-Step "Prerequisitos omitidos (-SkipPrereqs)."
  }

  if ($isIis) { Install-IIS } else { Install-Service }

  Invoke-EnsureDb
  Register-Updater $(if ($isIis) { "IIS" } else { "Service" })

  if ($script:NeedIisReset -and -not $DryRun) {
    Write-Step "Reiniciando IIS para cargar el Hosting Bundle..."
    & iisreset /noforce | Out-Null
  }

  $healthy = Wait-Healthy
  if ($healthy) {
    Write-Step "Instalación completada. Abre http://localhost:$Port - usuario inicial ECCSA / Qwe123456 (cámbialo)."
  } else {
    Write-Step "Instalación copiada, pero la verificación falló. Revisa $InstallPath\logs, el Visor de eventos y /configuration."
  }
  exit 0
}
catch {
  Write-Step "ERROR: $($_.Exception.Message)"
  Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
