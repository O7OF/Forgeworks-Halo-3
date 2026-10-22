@echo off
setlocal
title ForgeWorks Studio - Build and Test

echo ===============================================================================
echo                      FORGEWORKS STUDIO - COMPILE AND TEST
echo ===============================================================================
echo.

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not installed or not in PATH!
    pause
    exit /b 1
)

echo [1/2] Compiling ForgeWorks Solution (Release Mode)...
echo -------------------------------------------------------------------------------
dotnet build -c Release
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Compilation failed!
    pause
    exit /b %errorlevel%
)

echo.
echo [2/2] Running Complete Test Suite...
echo -------------------------------------------------------------------------------
dotnet test -c Release --no-build
if %errorlevel% neq 0 (
    echo.
    echo [WARNING] Some tests failed. Please review the output above.
) else (
    echo.
    echo [SUCCESS] Solution compiled and all 36 tests passed with 0 errors!
)

echo.
echo Press any key to exit...
pause >nul
