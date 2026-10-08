@echo off
chcp 65001 >nul
if exist "%LOCALAPPDATA%\KonturExport\logs" (
 start "" "%LOCALAPPDATA%\KonturExport\logs"
) else (
 echo Журнал появится после первого запуска.
 pause
)
