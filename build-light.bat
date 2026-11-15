@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -FrameworkDependent -OutputDir publish_light
exit /b %ERRORLEVEL%
