@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo   Building VramMonitor (Lightweight Single EXE)
echo   Mode: Framework-Dependent (Requires .NET 8)
echo ===================================================
echo.

set OUTPUT_DIR=publish_light

if exist "%OUTPUT_DIR%" (
    echo Cleaning existing output directory...
    rmdir /s /q "%OUTPUT_DIR%"
)

echo Publishing lightweight single-file executable...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo   Build Successful!
echo   Output: %~dp0%OUTPUT_DIR%\VramMonitor.exe
echo ===================================================
echo.

pause
