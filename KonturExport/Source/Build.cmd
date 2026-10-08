@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Установите .NET 8 SDK или новее с поддержкой net8.0.
  pause
  exit /b 1
)
dotnet publish "%~dp0KonturExport.csproj" -c Release -r win-x64 --self-contained true -p:PlaywrightPlatform=win -o "%~dp0publish"
set "result=%errorlevel%"
if "%result%"=="0" echo Готовая сборка: %~dp0publish
pause
exit /b %result%
