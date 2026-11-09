@echo off
rem Builds Urras and opens it; the window stays open if this one is closed or stopped with Ctrl+C
setlocal
cd /d "%~dp0"

rem A running Urras locks its files, so it's closed first: politely, then forced if it hasn't gone after a while
set CLOSE_WAIT_SECONDS=5
tasklist /fi "imagename eq Urras.exe" 2>nul | find /i "Urras.exe" >nul
if errorlevel 1 goto build

set answer=
set /p "answer=Urras is open and must close to be rebuilt. Close it? [Y/n] "
if /i "%answer%"=="n" exit /b 1
if /i "%answer%"=="no" exit /b 1

taskkill /im Urras.exe >nul 2>&1
for /l %%i in (1,1,%CLOSE_WAIT_SECONDS%) do (
    timeout /t 1 /nobreak >nul
    tasklist /fi "imagename eq Urras.exe" 2>nul | find /i "Urras.exe" >nul || goto build
)
taskkill /f /im Urras.exe >nul 2>&1
timeout /t 1 /nobreak >nul

:build

echo Building Urras...
dotnet build src\Urras.App -c Release -v q --nologo
if errorlevel 1 (
    echo.
    echo The build failed; the errors are above.
    pause
    exit /b 1
)

rem Urras opens in its own window; if it's gone again a few seconds later it crashed, and its log says why
set STARTUP_CHECK_SECONDS=4
echo Opening Urras...
start "" "src\Urras.App\bin\Release\net10.0\Urras.exe"
timeout /t %STARTUP_CHECK_SECONDS% /nobreak >nul
tasklist /fi "imagename eq Urras.exe" 2>nul | find /i "Urras.exe" >nul
if not errorlevel 1 (
    echo Urras is open.
    exit /b 0
)

echo.
echo Urras closed right after starting.
set "CRASH_LOG=%APPDATA%\Urras\crash.log"
if exist "%CRASH_LOG%" (
    echo The end of %CRASH_LOG%:
    echo.
    powershell -NoProfile -Command "Get-Content -LiteralPath $env:CRASH_LOG -Tail 40"
) else (
    echo It left no crash.log; Windows' Event Viewer may have it under Windows Logs ^> Application.
)
pause
exit /b 1
