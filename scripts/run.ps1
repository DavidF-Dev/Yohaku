<#
.SYNOPSIS
  Build (Release) and launch Yohaku.

.DESCRIPTION
  Builds the solution in Release and starts the tray app detached. Yohaku is
  single-instance, so re-running this while it's already running is a no-op
  (the new process exits immediately and the existing tray icon stays).

.PARAMETER NoBuild
  Skip the build and just launch the existing Release binary.

.PARAMETER Configuration
  Build configuration to build/launch. Defaults to Release.

.EXAMPLE
  .\scripts\run.ps1
  .\scripts\run.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Repo root is the parent of this script's folder, regardless of cwd.
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root "src\Yohaku\bin\$Configuration\net8.0-windows\yohaku.exe"

if (-not $NoBuild) {
    Write-Host "Building Yohaku ($Configuration)..." -ForegroundColor Cyan
    dotnet build (Join-Path $root 'Yohaku.slnx') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $exe)) {
    throw "Executable not found at '$exe'. Run without -NoBuild first."
}

Write-Host "Launching $exe" -ForegroundColor Green
Start-Process -FilePath $exe
Write-Host "Yohaku is running in the system tray. Right-click the tray icon for options." -ForegroundColor Green
