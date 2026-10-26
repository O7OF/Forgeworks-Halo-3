@echo off
setlocal
title ForgeWorks Studio v2.4.4 - Standalone Executable Publisher

echo ===============================================================================
echo            FORGEWORKS STUDIO - COMPILE TO STANDALONE EXECUTABLE (WIN-X64)
echo ===============================================================================
echo.

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not installed or not in PATH!
    pause
    exit /b 1
)

echo [1/2] Compiling and Publishing Standalone ForgeWorks.UI.exe (win-x64)...
echo -------------------------------------------------------------------------------
dotnet publish src\ForgeWorks.UI\ForgeWorks.UI.csproj -c Release -r win-x64 --self-contained -o publish

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Publish compilation failed!
    pause
    exit /b %errorlevel%
)

echo.
echo [2/2] Verifying Executable...
echo -------------------------------------------------------------------------------
if exist "publish\ForgeWorks.UI.exe" (
    echo [SUCCESS] Standalone executable compiled successfully!
    echo Location: %~dp0publish\ForgeWorks.UI.exe
) else (
    echo [WARNING] Expected executable not found at publish\ForgeWorks.UI.exe
)

echo.
echo Press any key to exit...
pause >nul
