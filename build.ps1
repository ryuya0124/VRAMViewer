<#
.SYNOPSIS
    Builds VramMonitor into a single standalone .exe file.
.PARAMETER SelfContained
    Includes the .NET Runtime in the EXE (default: $true). Set to $false if you only want a lightweight framework-dependent build.
.PARAMETER OutputDir
    Output directory (default: "publish").
#>
param (
    [switch]$FrameworkDependent = $false,
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$SelfContained = -not $FrameworkDependent
$SelfContainedStr = if ($SelfContained) { "true" } else { "false" }

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  Building VramMonitor (Single-File Win-x64 EXE)   " -ForegroundColor Cyan
Write-Host "  SelfContained: $SelfContainedStr" -ForegroundColor Gray
Write-Host "  OutputDir:     $OutputDir" -ForegroundColor Gray
Write-Host "===================================================" -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Write-Host "Cleaning existing output directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
}

$publishArgs = @(
    "publish",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $SelfContainedStr,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $OutputDir
)

if ($SelfContained) {
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

Write-Host "Running dotnet $($publishArgs -join ' ')..." -ForegroundColor Gray
& dotnet @publishArgs

if ($LASTEXITCODE -eq 0) {
    $exePath = Join-Path (Join-Path $ScriptDir $OutputDir) "VramMonitor.exe"
    Write-Host ""
    Write-Host "===================================================" -ForegroundColor Green
    Write-Host "  Build Successful!" -ForegroundColor Green
    Write-Host "  Output: $exePath" -ForegroundColor Green
    if (Test-Path $exePath) {
        $sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
        Write-Host "  File Size: $sizeMB MB" -ForegroundColor Green
    }
    Write-Host "===================================================" -ForegroundColor Green
} else {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}
