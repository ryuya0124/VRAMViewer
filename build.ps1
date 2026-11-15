param(
    [switch]$FrameworkDependent,
    [string]$OutputDir = 'publish'
)
$ErrorActionPreference = 'Stop'
$taskOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDir))
# Preserve portable language packs and settings when publishing again.
& dotnet publish (Join-Path $PSScriptRoot 'VramMonitor.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained (-not $FrameworkDependent).ToString().ToLowerInvariant() -p:PublishSingleFile=false -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
Write-Host "WinUI 3 portable app: $taskOutput\VramMonitor.exe"
Write-Host 'Keep the DLL files and Languages folder beside the executable.'
