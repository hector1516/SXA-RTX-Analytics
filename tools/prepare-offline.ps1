<#
.SYNOPSIS
  Descarga los prerequisitos offline de SXA-RTX Analytics (ASP.NET Core 10 Hosting Bundle)
  en installer\prereqs para incluirlos en el instalador.

.DESCRIPTION
  Ejecuta este script en una maquina con internet. Luego recompila el instalador con Inno Setup.
  El instalador resultante sera autocontenido y podra instalarse en servidores sin internet.

.PARAMETER OutputDir
  Carpeta de salida. Por defecto ..\installer\prereqs respecto al script.

.PARAMETER HostingBundleVersion
  Version del Hosting Bundle. Por defecto 10.0.0.
#>
param(
  [string]$OutputDir = "",
  [string]$HostingBundleVersion = "10.0.0"
)

$ErrorActionPreference = "Stop"

if (-not $OutputDir) {
  $OutputDir = Join-Path (Split-Path -Parent $PSScriptRoot) "installer\prereqs"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$url = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/$HostingBundleVersion/dotnet-hosting-$HostingBundleVersion-win.exe"
$out = Join-Path $OutputDir "dotnet-hosting-$HostingBundleVersion-win.exe"

Write-Host "Descargando Hosting Bundle $HostingBundleVersion ..."
Write-Host "URL: $url"
Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
$size = (Get-Item $out).Length / 1MB
Write-Host "Descargado: $out ($([math]::Round($size,1)) MB)"

# Verificar firma/hash opcional
Write-Host ""
Write-Host "Listo. Recompila el instalador con Inno Setup:"
Write-Host "  iscc /DMyAppVersion=x.y.z installer\Analytics.iss"
Write-Host "El instalador incluira installer\prereqs y sera autocontenido."