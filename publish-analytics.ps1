param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
Set-Location $root
Write-Host "Publicando SXA-RTX Analytics v$Version..."
Remove-Item -Recurse -Force "artifacts\publish" -ErrorAction SilentlyContinue
dotnet publish src\SXA.RTX.Analytics.Web\SXA.RTX.Analytics.Web.csproj -c Release -o artifacts\publish /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló" }
# Intentar compilar instalador si ISCC está instalado
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($iscc) {
  Write-Host "Compilando instalador Inno..."
  & $iscc.Source "/DMyAppVersion=$Version" "installer\Analytics.iss"
} else {
  Write-Host "ISCC no encontrado - solo publish en artifacts\publish. Instala Inno Setup para generar EXE."
}
Write-Host "Listo. Artefactos en artifacts\"
Get-ChildItem artifacts -Recurse | Select-Object FullName,Length | Format-Table -AutoSize
