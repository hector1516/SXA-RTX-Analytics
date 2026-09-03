param(
  [string]$InstallPath = "C:\Program Files\SXA-RTX Analytics",
  [ValidateSet("install","uninstall")]
  [string]$Mode = "install"
)

function Test-IsIIS {
  return (Get-WindowsFeature -Name Web-Server -ErrorAction SilentlyContinue | Where-Object Installed) -or (Get-Service -Name W3SVC -ErrorAction SilentlyContinue)
}

if ($Mode -eq "uninstall") {
  Write-Host "Desinstalando SXA-RTX Analytics..."
  # Detener servicio si existe
  sc.exe stop "SXA-RTX-Analytics" 2>$null
  sc.exe delete "SXA-RTX-Analytics" 2>$null
  # Eliminar sitio IIS si existe
  try { Import-Module WebAdministration -ErrorAction SilentlyContinue; if (Get-Website -Name "SXA-RTX-Analytics" -ErrorAction SilentlyContinue) { Remove-Website -Name "SXA-RTX-Analytics"; Remove-WebAppPool -Name "SXA-RTX-Analytics" } } catch {}
  exit 0
}

Write-Host "Instalando SXA-RTX Analytics en $InstallPath (Modo: $(if(Test-IsIIS){'IIS'}else{'Windows Service'}))"

# Crear logs
New-Item -ItemType Directory -Force -Path "$InstallPath\logs" | Out-Null

if (Test-IsIIS) {
  Write-Host "IIS detectado - configurando sitio..."
  Import-Module WebAdministration -ErrorAction Stop
  # Hosting Bundle check
  $bundle = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\ASP.NET Core\*" -ErrorAction SilentlyContinue
  if (-not $bundle) { Write-Warning "Instala ASP.NET Core Hosting Bundle 10.0 y haz iisreset si no está instalado." }

  if (-not (Test-Path "IIS:\AppPools\SXA-RTX-Analytics")) {
    New-WebAppPool -Name "SXA-RTX-Analytics" | Out-Null
    Set-ItemProperty -Path "IIS:\AppPools\SXA-RTX-Analytics" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty -Path "IIS:\AppPools\SXA-RTX-Analytics" -Name processModel.identityType -Value ApplicationPoolIdentity
  }
  if (Get-Website -Name "SXA-RTX-Analytics" -ErrorAction SilentlyContinue) { Remove-Website -Name "SXA-RTX-Analytics" }
  New-Website -Name "SXA-RTX-Analytics" -PhysicalPath $InstallPath -ApplicationPool "SXA-RTX-Analytics" -Port 5000 -Force | Out-Null
  Write-Host "Sitio IIS http://localhost:5000 creado. Configura HTTPS y ConnectionStrings__ConfigurationDatabase como variable del AppPool."
  iisreset /noforce
} else {
  Write-Host "IIS no detectado - instalando como Windows Service en http://localhost:5000"
  $exe = Join-Path $InstallPath "SXA.RTX.Analytics.Web.exe"
  if (Get-Service -Name "SXA-RTX-Analytics" -ErrorAction SilentlyContinue) { sc.exe stop "SXA-RTX-Analytics" | Out-Null; sc.exe delete "SXA-RTX-Analytics" | Out-Null; Start-Sleep 2 }
  sc.exe create "SXA-RTX-Analytics" binPath= "`"$exe`" --urls http://0.0.0.0:5000" DisplayName= "SXA-RTX Analytics" start= auto | Out-Null
  sc.exe failure "SXA-RTX-Analytics" reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
  sc.exe start "SXA-RTX-Analytics" | Out-Null
  Write-Host "Servicio iniciado. Abre http://localhost:5000"
  # Abrir firewall
  netsh advfirewall firewall add rule name="SXA-RTX Analytics" dir=in action=allow protocol=TCP localport=5000 2>$null
}

Write-Host "Instalación completada. Usa /configuration para pegar la cadena SQL. Copia este mismo instalador a otro equipo para migrar, y usa Import/Export JSON para llevar la configuración."
