@echo off
rem Runs the core tests, the interface tests and the standards check on unpushed commits, then sums them up
rem "test core" or "test ui" runs just that one
setlocal
cd /d "%~dp0"

set failed=
set only=%~1

if not "%only%"=="" if /i not "%only%"=="core" if /i not "%only%"=="ui" (
    echo Usage: test [core^|ui]
    exit /b 1
)

if /i not "%only%"=="ui" (
    echo === Core tests ===
    dotnet run --project tests\Urras.Core.Tests -c Release
    if errorlevel 1 set failed=%failed% core
    echo.
)

if /i not "%only%"=="core" (
    echo === Interface tests ===
    dotnet test tests\Urras.App.Tests -c Release --nologo -v q
    if errorlevel 1 set failed=%failed% interface
    echo.
)

if "%only%"=="" (
    echo === Coding standards of unpushed commits ===
    dotnet run --project tools\Urras.Standards -v q -- --unpushed
    if errorlevel 1 set failed=%failed% standards
    echo.
)

if defined failed (
    echo FAILED:%failed%
    pause
    exit /b 1
)
echo Everything passed.
pause
