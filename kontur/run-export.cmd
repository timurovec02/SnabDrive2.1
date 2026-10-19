@echo off
REM ============================================================================
REM  Обёртка для запуска KonturExport.exe ( headed-браузер в интерактивной сессии).
REM  Аргументы передаёт задача Планировщика (или вы вручную):
REM     run-export.cmd <favorites|templates> "<папка выгрузки>"
REM  Пример:  run-export.cmd favorites "C:\KonturExport\out\favorites"
REM
REM  Обёртка:
REM   - запускает экспортёр с нужным режимом и папкой;
REM   - пишет ВЕСЬ вывод в export-run.log (рядом с .exe) — видно, что произошло;
REM   - пишет код возврата в export-status.txt — сайт читает его как статус.
REM
REM  НАСТРОЙТЕ путь к KonturExport.exe ниже.
REM ============================================================================
setlocal EnableExtensions

set "EXE=C:\kontur\export\KonturExport.exe"

REM --- аргументы: режим и папка выгрузки ---
set "MODE=%~1"
set "OUT=%~2"
if "%MODE%"=="" set "MODE=favorites"
if /I "%MODE%"=="templates" (set "FLAG=--templates") else (set "FLAG=--favorites")
if "%OUT%"=="" set "OUT=%~dp0out\%MODE%"

REM --- пути рядом с экспортёром ---
for %%I in ("%EXE%") do set "LOG=%%~dpIexport-run.log"
for %%I in ("%EXE%") do set "STATUS=%%~dpIexport-status.txt"

>> "%LOG%" echo.
>> "%LOG%" echo ==== %DATE% %TIME% | режим=%MODE% | папка=%OUT% ====

if not exist "%EXE%" (
    >> "%LOG%" echo [run-export] ОШИБКА: экспортёр не найден: %EXE%
    > "%STATUS%" echo 127
    endlocal & exit /b 127
)

>> "%LOG%" echo [run-export] Запуск: "%EXE%" --no-pause %FLAG% --output "%OUT%"
"%EXE%" --no-pause %FLAG% --output "%OUT%" >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"

>> "%LOG%" echo [run-export] Код возврата: %RC%
> "%STATUS%" echo %RC%

endlocal & exit /b %RC%
