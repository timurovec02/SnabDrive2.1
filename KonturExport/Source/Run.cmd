@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Для исходников нужен .NET 8 SDK. Готовый EXE в основном архиве SDK не требует.
  pause
  exit /b 1
)
if exist "%~dp0..\settings.json" (
  dotnet run --project "%~dp0KonturExport.csproj" -- --settings "%~dp0..\settings.json" %*
) else (
  dotnet run --project "%~dp0KonturExport.csproj" -- --settings "%~dp0settings.json" %*
)
set "result=%errorlevel%"
pause
exit /b %result%
