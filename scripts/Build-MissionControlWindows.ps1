<#
.SYNOPSIS
Builds the Mission Control MAUI app for Windows without installing it.

.DESCRIPTION
Runs dotnet build for the checked-in Windows target and reports the build
output directory. The script does not publish, package, launch, or install the
application.

.EXAMPLE
.\scripts\Build-MissionControlWindows.ps1

Builds a Release configuration for win-x64.

.EXAMPLE
.\scripts\Build-MissionControlWindows.ps1 -Configuration Debug

Builds a Debug configuration for win-x64.

.EXAMPLE
.\scripts\Build-MissionControlWindows.ps1 -RuntimeIdentifier win-arm64

Builds a Release configuration for Windows on ARM64.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter()]
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [Parameter()]
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)) {
    throw "The Windows MAUI target must be built on Windows."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH."
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDirectory
$projectPath = Join-Path `
    $repoRoot `
    "MissionControl.Mobile\MissionControl.Mobile.csproj"
$targetFramework = "net10.0-windows10.0.19041.0"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Mission Control MAUI project was not found: $projectPath"
}

$buildArguments = @(
    "build"
    $projectPath
    "--framework"
    $targetFramework
    "--configuration"
    $Configuration
    "-p:RuntimeIdentifierOverride=$RuntimeIdentifier"
    "-p:WindowsPackageType=None"
)

if ($NoRestore) {
    $buildArguments += "--no-restore"
}

Write-Host
Write-Host "==> Building Mission Control for Windows" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $RuntimeIdentifier"

& dotnet @buildArguments

if ($LASTEXITCODE -ne 0) {
    throw "Windows MAUI build failed. Exit code: $LASTEXITCODE"
}

$outputPath = Join-Path `
    $repoRoot `
    "MissionControl.Mobile\bin\$Configuration\$targetFramework\$RuntimeIdentifier"

Write-Host
Write-Host "Mission Control Windows build succeeded." -ForegroundColor Green
Write-Host "Output: $outputPath"
