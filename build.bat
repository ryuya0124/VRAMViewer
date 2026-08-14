@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo   Building VramMonitor (Single-File Win-x64 EXE)
echo ===================================================
echo.

set OUTPUT_DIR=publish

if exist "%OUTPUT_DIR%" (
    echo Cleaning existing output directory...
    rmdir /s /q "%OUTPUT_DIR%"
)

echo Publishing self-contained single-file executable...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT_DIR%"

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
