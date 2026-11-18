param(
  [string]$Version = "1.0.0",
  [switch]$SkipInstaller,
  [switch]$SkipRestore,
  [switch]$UploadRelease
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
Set-Location $root

Write-Host "Publicando SXA-RTX Analytics v$Version..."
Remove-Item -Recurse -Force "artifacts\publish" -ErrorAction SilentlyContinue

$proj = "src\SXA.RTX.Analytics.Web\SXA.RTX.Analytics.Web.csproj"
if (-not $SkipRestore) {
  dotnet restore $proj
  if ($LASTEXITCODE -ne 0) { throw "dotnet restore fallo" }
}
dotnet publish $proj -c Release -o artifacts\publish /p:Version=$Version /p:VersionPrefix=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo" }

if (-not (Test-Path "artifacts\publish\SXA.RTX.Analytics.Web.exe")) {
  throw "artifacts\publish\SXA.RTX.Analytics.Web.exe no existe tras el publish"
}

if ($SkipInstaller) {
  Write-Host "SkipInstaller: solo publish en artifacts\publish."
} else {
  $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if (-not $iscc) {
    foreach ($cand in @(
      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
      "C:\Program Files\Inno Setup 6\ISCC.exe"
    )) { if (Test-Path $cand) { $iscc = [pscustomobject]@{ Source = $cand }; break } }
  }
  if ($iscc) {
    Write-Host "Compilando instalador Inno con $($iscc.Source)..."
    & $iscc.Source "/DMyAppVersion=$Version" "installer\Analytics.iss"
    if ($LASTEXITCODE -ne 0) { Write-Warning "ISCC devolvio codigo $LASTEXITCODE (revisar log de Inno). Continua con el publish." }
  } else {
    Write-Warning "ISCC no encontrado - solo publish en artifacts\publish. Instala Inno Setup 6 para generar el EXE."
  }
}

# Generar ZIP de publicacion (para auto-actualizacion)
$zipPath = "artifacts\SXA-RTX-Analytics-v$Version-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Comprimiendo artifacts\publish en $zipPath ..."
Compress-Archive -Path "artifacts\publish\*" -DestinationPath $zipPath -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "ZIP generado: $zipPath ($zipSize MB)"

# Subir a GitHub Release si se solicita
if ($UploadRelease) {
  $gh = Get-Command gh -ErrorAction SilentlyContinue
  if (-not $gh) {
    Write-Warning "gh (GitHub CLI) no encontrado. Sube manualmente los archivos al release."
  } else {
    $tag = "v$Version"
    $installerPath = "artifacts\pkg\Setup_SXA_RTX_Analytics_v$Version.exe"
    Write-Host "Creando release $tag en GitHub..."
    & $gh.Source release create $tag $installerPath $zipPath --title "SXA-RTX Analytics $Version" --notes "Release $Version" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
      Write-Host "Release $tag creado con installer y ZIP."
    } else {
      # El release puede ya existir; intentar subir assets
      & $gh.Source release upload $tag $installerPath $zipPath --clobber 2>&1 | Out-Null
      if ($LASTEXITCODE -eq 0) { Write-Host "Assets subidos al release $tag." }
      else { Write-Warning "No se pudo subir al release. Revisa gh auth status." }
    }
  }
}

Write-Host "Listo. Artefactos en artifacts\"
Get-ChildItem artifacts -Recurse -File | Measure-Object -Property Length -Sum |
  ForEach-Object { Write-Host ("  {0} archivos, {1:N1} MB" -f $_.Count, ($_.Sum / 1MB)) }
Get-ChildItem artifacts\pkg -ErrorAction SilentlyContinue | Select-Object Name, Length | Format-Table -AutoSize
