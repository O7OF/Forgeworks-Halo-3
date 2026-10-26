@echo off
setlocal
title ForgeWorks Studio v2.4.3 - Halo 3 Blam Engine Desktop Editor

echo ===============================================================================
echo                      FORGEWORKS STUDIO - HALO 3 BLAM
echo               (Blam Editing Suite For Halo 3 v2.4.3)
echo ===============================================================================
echo.

:: 1. Verify .NET SDK is installed
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not installed or not in PATH!
    echo Please install .NET 10 or later from https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

:: 2. Check command-line argument
if /i "%~1"=="build" goto do_build
if /i "%~1"=="compile" goto do_build
if /i "%~1"=="test" goto do_test
if /i "%~1"=="publish" goto do_publish
if /i "%~1"=="debug" goto do_debug
if not "%~1"=="" goto do_run

:: Default: Run ForgeWorks Studio in Release mode
:do_run
echo [1/2] Building ForgeWorks (Release)...
dotnet build src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Release -v minimal
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed! Please review the errors above.
    echo.
    pause
    exit /b %errorlevel%
)

echo.
echo [2/2] Launching ForgeWorks Studio Viewport...
echo -------------------------------------------------------------------------------
dotnet run --project src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Release --no-build
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Application exited with error code %errorlevel%.
    echo.
    pause
    exit /b %errorlevel%
)
exit /b 0

:do_debug
echo [1/2] Building ForgeWorks (Debug)...
dotnet build src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Debug -v minimal
if %errorlevel% neq 0 (
    echo [ERROR] Debug build failed!
    pause
    exit /b %errorlevel%
)
echo [2/2] Launching ForgeWorks Studio in Debug Mode...
dotnet run --project src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Debug --no-build
pause
exit /b 0

:do_build
echo Compiling all ForgeWorks projects (Core, Blam, UI, Tests)...
dotnet build -c Release
if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] Compilation succeeded with 0 errors!
) else (
    echo.
    echo [FAILED] Compilation failed!
)
pause
exit /b %errorlevel%

:do_test
echo Running ForgeWorks Automated Test Suite...
dotnet test -c Release
pause
exit /b %errorlevel%

:do_publish
echo Publishing standalone ForgeWorks executable (win-x64)...
dotnet publish src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Release -r win-x64 --self-contained -o publish
if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] Standalone app published to: %~dp0publish\ForgeWorks.UI.exe
)
pause
exit /b %errorlevel%
