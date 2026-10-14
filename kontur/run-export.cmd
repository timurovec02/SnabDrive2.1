@echo off
REM ============================================================================
REM  Обёртка для задачи Планировщика заданий Windows.
REM  Веб-приложение (IIS, Session 0) не может открыть браузер, поэтому экспортёр
REM  запускается ЭТОЙ задачей в интерактивной сессии залогиненного пользователя.
REM
REM  Как работает:
REM   1. Сайт пишет аргументы (--no-pause --favorites/--templates --output "...")
REM      в файл export-request.txt (параметр Kontur:RequestFile).
REM   2. Сайт триггерит задачу:  schtasks /run /tn "SnabDriveKontur"
REM   3. Задача выполняет этот файл -> он читает аргументы и запускает KonturExport.exe.
REM
REM  НАСТРОЙТЕ путь к KonturExport.exe ниже (REQ выведется автоматически рядом с ним).
REM ============================================================================
setlocal EnableExtensions

set "EXE=C:\kontur\export\KonturExport.exe"

REM Файл запроса лежит рядом с экспортёром (совпадает с Kontur:RequestFile по умолчанию)
for %%I in ("%EXE%") do set "REQ=%%~dpIexport-request.txt"

if not exist "%EXE%" (
    echo [run-export] Экспортёр не найден: %EXE%
    exit /b 1
)
if not exist "%REQ%" (
    echo [run-export] Файл запроса не найден: %REQ%
    exit /b 1
)

set /p ARGS=<"%REQ%"
echo [run-export] Запуск: "%EXE%" %ARGS%
"%EXE%" %ARGS%
set "RC=%ERRORLEVEL%"
echo [run-export] Код возврата: %RC%

endlocal & exit /b %RC%
