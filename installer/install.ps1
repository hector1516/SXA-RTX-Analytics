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
  [ValidateSet("install","uninstall","diagnose")]
  [string]$Mode = "install",
  [int]$Port = 5000,
  [switch]$DryRun,
  [switch]$SkipPrereqs,
  [switch]$ForceIis,
  [switch]$Diagnose,
  [string]$WindowsSource = "",
  [string]$PrereqsPath = ""
)

$ErrorActionPreference = "Stop"

$SiteName     = "SXA-RTX-Analytics"
$AppPoolName  = "SXA-RTX-Analytics"
$ServiceName  = "SXA-RTX-Analytics"
$TaskName     = "SXA-RTX-Analytics-Updater"
$MyAppExeName = "SXA.RTX.Analytics.Web.exe"
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

function Resolve-PrereqsPath {
  if ($PrereqsPath -and (Test-Path $PrereqsPath)) { return $PrereqsPath }
  $candidates = @(
    (Join-Path (Split-Path -Parent $PSCommandPath) "prereqs"),
    (Join-Path $InstallPath "prereqs")
  )
  foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
  return $null
}

function Find-HostingBundleInstaller {
  $dir = Resolve-PrereqsPath
  if (-not $dir) { return $null }
  $files = Get-ChildItem -LiteralPath $dir -Filter "*HostingBundle*.exe" -ErrorAction SilentlyContinue
  if (-not $files) { $files = Get-ChildItem -LiteralPath $dir -Filter "dotnet-hosting-*.exe" -ErrorAction SilentlyContinue }
  if ($files) { return ($files | Select-Object -First 1).FullName }
  return $null
}

function Ensure-AspNetCoreRuntime {
  if (Test-AspNetCoreRuntime) { Write-Step "ASP.NET Core 10 detectado."; return }

  $local = Find-HostingBundleInstaller
  if ($local) {
    Invoke-Step "Instalando ASP.NET Core Hosting Bundle 10 desde prereq local: $local" {
      $p = Start-Process -FilePath $local -ArgumentList "/quiet /norestart" -Wait -PassThru
      if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "Instalador local fallo (codigo $($p.ExitCode))." }
      if (-not (Test-AspNetCoreRuntime)) { throw "El instalador local termino pero el runtime 10 no se detecta." }
      $script:NeedIisReset = $true
    }
    return
  }

  if (Get-Command winget -ErrorAction SilentlyContinue) {
    Invoke-Step "Instalando ASP.NET Core Hosting Bundle 10 (winget)" {
      & winget install --id Microsoft.DotNet.HostingBundle.10 -e --silent --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity
      if (-not (Test-AspNetCoreRuntime)) { throw "No se pudo instalar el Hosting Bundle (winget codigo $LASTEXITCODE)." }
      $script:NeedIisReset = $true
    }
    return
  }

  Invoke-Step "Descargando ASP.NET Core Hosting Bundle 10 desde Microsoft" {
    $tmp = Join-Path $env:TEMP "dotnet-hosting-10.exe"
    $url = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.0/dotnet-hosting-10.0.0-win.exe"
    try { Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing } catch { throw "No se pudo descargar el Hosting Bundle: $($_.Exception.Message). Colocalo en la carpeta prereqs." }
    $p = Start-Process -FilePath $tmp -ArgumentList "/quiet /norestart" -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "Instalador descargado fallo (codigo $($p.ExitCode))." }
    if (-not (Test-AspNetCoreRuntime)) { throw "Descarga termino pero el runtime 10 no se detecta." }
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

  $sourceArgs = @{}
  if ($WindowsSource -and (Test-Path $WindowsSource)) {
    $sourceArgs['Source'] = $WindowsSource
    Write-Step "Usando fuente de Windows: $WindowsSource"
  }

  Invoke-Step "Instalando features IIS: $($need -join ', ')" {
    try {
      Install-WindowsFeature -Name $need -IncludeManagementTools @sourceArgs | Out-Null
    } catch {
      Write-Step "Install-WindowsFeature fallo: $($_.Exception.Message). Intentando con DISM..."
      foreach ($feat in $need) {
        $dismArgs = @("/online","/enable-feature","/featurename:$feat","/all","/NoRestart")
        if ($WindowsSource) { $dismArgs += "/source:$WindowsSource" }
        & dism.exe @dismArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "DISM fallo para $feat (codigo $LASTEXITCODE). Proporciona -WindowsSource con la carpeta sources\sxs del ISO de Windows Server." }
      }
    }
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

  Invoke-Step "Desbloqueando archivos (quitar Mark-of-the-Web)" {
    Get-ChildItem -LiteralPath $InstallPath -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue }
  }
  Grant-AppPoolAccess
  Set-FirewallRule

  Invoke-Step "Verificando que el App Pool arranca" {
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    $state = (Get-WebAppPoolState -Name $AppPoolName).Value
    Write-Step "Estado del App Pool: $state"
    if ($state -ne "Started") {
      throw "El App Pool no arranco (estado=$state). Revisa que el Hosting Bundle 10 este instalado y que la identidad tenga permisos."
    }
  }
}

function Install-Service {
  $exe = Join-Path $InstallPath "SXA.RTX.Analytics.Web.exe"
  Invoke-Step "Desbloqueando archivos (quitar Mark-of-the-Web)" {
    Get-ChildItem -LiteralPath $InstallPath -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue }
  }
  Invoke-Step "Registrando servicio Windows $ServiceName (automático, con reinicio ante fallo)" {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
      Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
      $delOut = & sc.exe delete $ServiceName 2>&1
      Write-Step "  sc delete: $($delOut -join ' ')"
      Start-Sleep -Seconds 2
    }
    $createOut = & sc.exe create $ServiceName binPath= "`"$exe`" --urls http://0.0.0.0:$Port" DisplayName= "SXA-RTX Analytics" start= auto 2>&1
    Write-Step "  sc create: $($createOut -join ' ')"
    if ($LASTEXITCODE -ne 0) {
      Write-Step "  sc create fallo (codigo $LASTEXITCODE). Verificando si el servicio ya existe..."
      $existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
      if ($existingSvc) {
        Write-Step "  El servicio ya existe (estado=$($existingSvc.Status)). Actualizando binPath..."
        & sc.exe config $ServiceName binPath= "`"$exe`" --urls http://0.0.0.0:$Port" 2>&1 | Out-Null
        & sc.exe config $ServiceName start= auto 2>&1 | Out-Null
      } else {
        Write-Step "  Intentando con New-Service..."
        try {
          New-Service -Name $ServiceName -BinaryPathName "`"$exe`" --urls http://0.0.0.0:$Port" -DisplayName "SXA-RTX Analytics" -StartupType Automatic -ErrorAction Stop | Out-Null
          Write-Step "  New-Service OK"
        } catch {
          throw "No se pudo crear el servicio: $($_.Exception.Message)"
        }
      }
    }
    & sc.exe config $ServiceName obj= "LocalSystem" | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne 'Running') {
      $err = ""
      try {
        $ev = Get-WinEvent -FilterHashtable @{ LogName='Application' } -MaxEvents 5 -ErrorAction SilentlyContinue
        if ($ev) { $err = ($ev | Select-Object -First 3 | ForEach-Object { $_.Message }) -join " | " }
      } catch { }
      throw "El servicio no arranco (estado=$($svc.Status)). $err"
    }
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

function Set-DefaultSqlConnection {
  $prod = Join-Path $InstallPath "appsettings.Production.json"
  if (Test-Path $prod) { Write-Step "appsettings.Production.json ya existe; se respeta."; return }
  $base = Join-Path $InstallPath "appsettings.json"
  if (-not (Test-Path $base)) { Write-Step "No se encontró appsettings.json; se omite la conexión por defecto."; return }
  try {
    $raw = Get-Content -LiteralPath $base -Raw
    $m = [regex]::Match($raw, '"ConfigurationDatabase"\s*:\s*"([^"]*)"')
    $cs = if ($m.Success) { $m.Groups[1].Value } else { $null }
  } catch { $cs = $null }
  if ([string]::IsNullOrWhiteSpace($cs)) { Write-Step "appsettings.json no define ConnectionStrings:ConfigurationDatabase."; return }
  $json = [ordered]@{ ConnectionStrings = [ordered]@{ ConfigurationDatabase = $cs } } | ConvertTo-Json -Depth 4
  Invoke-Step "Dejando la conexión SQL por defecto (desde appsettings.json)" {
    Set-Content -LiteralPath $prod -Value $json -Encoding UTF8
  }
}

function Write-FirstLoginNote {
  $base = Join-Path $InstallPath "appsettings.json"
  $user = "ECCSA"; $pass = "Qwe123456"
  try {
    $raw = Get-Content -LiteralPath $base -Raw
    $mu = [regex]::Match($raw, '"Username"\s*:\s*"([^"]*)"')
    $mp = [regex]::Match($raw, '"DefaultPassword"\s*:\s*"([^"]*)"')
    if ($mu.Success -and $mu.Groups[1].Value) { $user = $mu.Groups[1].Value }
    if ($mp.Success -and $mp.Groups[1].Value) { $pass = $mp.Groups[1].Value }
  } catch { }
  $file = Join-Path $InstallPath "PRIMER-INGRESO.txt"
  $content = @"
SXA-RTX Analytics - Primer ingreso
==================================
URL:      http://localhost:$Port
Usuario:  $user
Password: $pass

Importante:
- Cambia la contraseña en el primer ingreso.
- Crea los usuarios adicionales en "Usuarios" (solo Administrador).
- Borra este archivo después de cambiar la contraseña.
"@
  Invoke-Step "Creando PRIMER-INGRESO.txt con las credenciales iniciales" {
    Set-Content -LiteralPath $file -Value $content -Encoding UTF8
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

function New-DesktopShortcut {
  param([string]$Url = "http://localhost:$Port")
  $shortcutName = "SXA-RTX Analytics.url"
  $content = "[InternetShortcut]`r`nURL=$Url`r`nIconIndex=0`r`n"
  $targets = @()
  try { $targets += Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) $shortcutName } catch { }
  try { $targets += Join-Path ([Environment]::GetFolderPath("Desktop")) $shortcutName } catch { }
  foreach ($t in $targets) {
    if (-not $t) { continue }
    try {
      Set-Content -LiteralPath $t -Value $content -Encoding ASCII
      Write-Step "Acceso directo creado: $t"
    } catch { Write-Step "No se pudo crear acceso directo en $t : $($_.Exception.Message)" }
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
      $delOut = & sc.exe delete $ServiceName 2>&1
      Write-Step "  sc delete: $($delOut -join ' ')"
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

  if ($Diagnose -or $Mode -eq "diagnose") {
    Write-Step "=== DIAGNOSTICO ==="
    Write-Step "Runtime .NET:"
    try {
      $dg = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction Stop
      Write-Step "  WDAC CodeIntegrity enforcement: $($dg.CodeIntegrityPolicyEnforcementStatus) (2=enforced)"
      Write-Step "  WDAC UserMode enforcement: $($dg.UsermodeCodeIntegrityPolicyEnforcementStatus) (2=enforced)"
      if ($dg.CodeIntegrityPolicyEnforcementStatus -eq 2 -or $dg.UsermodeCodeIntegrityPolicyEnforcementStatus -eq 2) {
        Write-Step "  AVISO: WDAC activo. Los assemblies no firmados pueden ser bloqueados (error 0x800711C7)."
      }
    } catch { Write-Step "  WDAC: no disponible" }
    try { & dotnet --list-runtimes 2>&1 | ForEach-Object { Write-Step "  $_" } } catch { Write-Step "  dotnet no disponible" }
    Write-Step "Servicio W3SVC:"
    $svc = Get-Service W3SVC -ErrorAction SilentlyContinue
    Write-Step "  $(if ($svc) { $svc.Status } else { 'NO instalado' })"
    Write-Step "IIS features:"
    foreach ($f in @('Web-Server','Web-WebSockets')) {
      try { $fi = Get-WindowsFeature -Name $f -ErrorAction Stop; Write-Step "  $f = $($fi.Installed)" } catch { Write-Step "  $f = n/a (no WindowsFeature)" }
    }
    Write-Step "Sitio IIS:"
    try {
      Import-Module WebAdministration -ErrorAction Stop
      $ws = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
      Write-Step "  $(if ($ws) { "Existe: $($ws.State) en $($ws.bindings.Collection.bindingInformation)" } else { 'NO existe' })"
      $ap = Get-WebAppPoolState -Name $AppPoolName -ErrorAction SilentlyContinue
      Write-Step "  AppPool: $(if ($ap) { $ap.Value } else { 'NO existe' })"
    } catch { Write-Step "  WebAdministration no disponible: $($_.Exception.Message)" }
    Write-Step "Puerto ${Port}:"
    $listeners = netstat -ano 2>$null | Select-String ":$Port\s"
    if ($listeners) { $listeners | ForEach-Object { Write-Step "  $_" } } else { Write-Step "  No hay listener en $Port" }
    Write-Step "Archivos de la app:"
    Write-Step "  InstallPath=$InstallPath (existe=$(Test-Path $InstallPath))"
    Write-Step "  exe=$($exe = Join-Path $InstallPath $MyAppExeName 2>$null; Test-Path $exe)"
    Write-Step "  data=$((Test-Path (Join-Path $InstallPath 'data'))) logs=$((Test-Path (Join-Path $InstallPath 'logs')))"
    Write-Step "  prereqs=$PrereqsPath"
    Write-Step "=== FIN DIAGNOSTICO ==="
    exit 0
  }

  if (-not $DryRun -and -not (Test-Admin)) { throw "Ejecuta este script como Administrador." }

  if ($Mode -eq "uninstall") { Uninstall; exit 0 }

  $isIis = Test-IsIIS
  Write-Step ("Modo de ejecución detectado: " + $(if ($isIis) { "IIS" } else { "Windows Service" }))

  Ensure-Directories
  Set-DefaultSqlConnection

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

  New-DesktopShortcut -Url "http://localhost:$Port"
  $healthy = Wait-Healthy
  if ($healthy) {
    Write-Step "Instalación completada. Abre http://localhost:$Port - usuario inicial ECCSA / Qwe123456 (cámbialo)."
    Write-Step "Credenciales iniciales guardadas en $InstallPath\PRIMER-INGRESO.txt"
    Write-FirstLoginNote
    try { Start-Process "http://localhost:$Port" } catch { }
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
