@echo off
chcp 65001 >nul
cd /d "%~dp0"
"%~dp0KonturExport.exe" --setup-plugin %*
set "result=%errorlevel%"
echo.
pause
exit /b %result%
