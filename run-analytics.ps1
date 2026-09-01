Set-Location "D:\Antigravity\SXA-RTX\SXA-RTX-Analytics"
Write-Host "Iniciando SXA-RTX Analytics en http://localhost:5149 ..." -ForegroundColor Cyan
dotnet run --project "src\SXA.RTX.Analytics.Web\SXA.RTX.Analytics.Web.csproj" --urls "http://0.0.0.0:5149"
