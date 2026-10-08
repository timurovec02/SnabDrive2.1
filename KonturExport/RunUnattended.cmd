@echo off
chcp 65001 >nul
cd /d "%~dp0"
"%~dp0KonturExport.exe" --headless --no-pause %*
set "result=%errorlevel%"
exit /b %result%
