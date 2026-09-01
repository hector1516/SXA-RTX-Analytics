@echo off
cd /d "D:\Antigravity\SXA-RTX\SXA-RTX-Analytics"
echo Iniciando SXA-RTX Analytics en http://localhost:5149 ...
dotnet run --project "src\SXA.RTX.Analytics.Web\SXA.RTX.Analytics.Web.csproj" --urls "http://0.0.0.0:5149"
pause
