@echo off
rem Builds Urras and opens it; the window stays open if this one is closed or stopped with Ctrl+C
setlocal
cd /d "%~dp0"

tasklist /fi "imagename eq Urras.exe" 2>nul | find /i "Urras.exe" >nul
if not errorlevel 1 (
    echo Urras is already open. Close it first so it can be rebuilt.
    pause
    exit /b 1
)

echo Building Urras...
dotnet build src\Urras.App -c Release -v q --nologo
if errorlevel 1 (
    echo.
    echo The build failed; the errors are above.
    pause
    exit /b 1
)

start "" "src\Urras.App\bin\Release\net10.0\Urras.exe"
