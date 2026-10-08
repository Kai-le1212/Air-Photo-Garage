@echo off
REM ============================================================
REM  Air Photo Garage - Launcher
REM  Usage: double-click, or run  run.cmd [option]
REM
REM  Options:
REM    (none)      Start with Debug + packaged profile (daily dev)
REM    /release    Start with Release configuration
REM    /build      Build only, do not launch
REM    /unpackaged Launch as unpackaged app (no MSIX identity)
REM ============================================================
setlocal enabledelayedexpansion

cd /d "%~dp0"

set CONFIG=Debug
set MODE=pkg
set DO_RUN=1

:parse
if "%~1"=="" goto run
if /i "%~1"=="/release"    set CONFIG=Release
if /i "%~1"=="/build"      set DO_RUN=0
if /i "%~1"=="/unpackaged" set MODE=unpkg
shift
goto parse

:run
echo ============================================================
echo  Air Photo Garage  ^|  !CONFIG!  ^|  !MODE!
echo ============================================================
echo.

if "!DO_RUN!"=="0" goto buildonly
if /i "!MODE!"=="unpkg" goto launchunpkg
goto launchpkg

:buildonly
echo [*] Building...
dotnet build -c !CONFIG!
echo.
echo [+] Done. Exit code: !ERRORLEVEL!
goto end

:launchpkg
echo [*] Launching (packaged)...
dotnet run -c !CONFIG! --launch-profile "AirPhotoGarage (Package)"
echo.
echo [+] App exited. Exit code: !ERRORLEVEL!
goto end

:launchunpkg
echo [*] Launching (unpackaged)...
dotnet run -c !CONFIG! --launch-profile "AirPhotoGarage (Unpackaged)"
echo.
echo [+] App exited. Exit code: !ERRORLEVEL!
goto end

:end
endlocal
echo.
pause
exit /b 0
