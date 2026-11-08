@echo off
rem Checks files and commit messages against CODING_STANDARDS.md; run with --help for the options
setlocal
cd /d "%~dp0"

if not "%~1"=="" (
    dotnet run --project tools\Urras.Standards -v q -- %*
    goto end
)

echo What should be checked?
echo   1^) Uncommitted changes
echo   2^) The last commit
echo   3^) Commits not pushed yet
echo   4^) Every commit
echo   5^) Commits after a commit
echo   6^) Commits since a date
echo   7^) Every file as it is now
echo   8^) Check every commit automatically from now on
set /p "choice=Choice: "

set option=
if "%choice%"=="1" set option=--uncommitted
if "%choice%"=="2" set option=--last
if "%choice%"=="3" set option=--unpushed
if "%choice%"=="4" set option=--history
if "%choice%"=="7" set option=--all
if "%choice%"=="8" set option=--install-hooks
if "%choice%"=="5" (
    set /p "value=Commit (hash, tag or branch): "
    set option=--since-commit
)
if "%choice%"=="6" (
    set /p "value=Date (such as 2026-11-01 or 2 weeks ago): "
    set option=--since-date
)

if "%option%"=="" (
    echo No such choice.
    goto end
)

if defined value (
    dotnet run --project tools\Urras.Standards -v q -- %option% "%value%"
) else (
    dotnet run --project tools\Urras.Standards -v q -- %option%
)

:end
echo.
pause
endlocal
