<#
.SYNOPSIS
    Build a distributable, self-contained Yohaku.exe.

.DESCRIPTION
    Publishes a self-contained, single-file, compressed win-x64 build and stages it as
    dist/Yohaku-<version>.exe (version read from src/Yohaku/Yohaku.csproj, the single
    source of truth). Prints the SHA-256 so a release can advertise it. Runs the unit
    tests first unless -SkipTests.

    The result needs no .NET runtime installed; it is a bare exe meant to be run as-is.

.PARAMETER SkipTests
    Skip the unit-test gate (off by default).

.EXAMPLE
    .\scripts\publish.ps1
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host "publish: $message" -ForegroundColor Red
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj   = Join-Path $repoRoot 'src\Yohaku\Yohaku.csproj'
$slnx     = Join-Path $repoRoot 'Yohaku.slnx'

# Version: single source of truth is <Version> in the csproj.
$match = [regex]::Match((Get-Content $csproj -Raw -Encoding UTF8), '<Version>(.+?)</Version>')
if (-not $match.Success) { Fail "could not find <Version> in $csproj" }
$version = $match.Groups[1].Value

if (-not $SkipTests) {
    Write-Host 'Running unit tests...' -ForegroundColor Cyan
    dotnet test $slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'unit tests failed' }
}

$publishDir = Join-Path $repoRoot 'src\Yohaku\bin\publish'
$distDir    = Join-Path $repoRoot 'dist'

# Clean the publish dir so no stale output can be mistaken for this build.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Publishing self-contained single-file build for v$version..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed' }

$built = Join-Path $publishDir 'Yohaku.exe'
if (-not (Test-Path $built)) { Fail "expected $built not found after publish" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$asset = Join-Path $distDir "Yohaku-$version.exe"
Copy-Item $built $asset -Force

$sha    = (Get-FileHash $asset -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item $asset).Length / 1MB, 1)

Write-Host ''
Write-Host "Built: $asset" -ForegroundColor Green
Write-Host "  Size    : $sizeMb MB"
Write-Host "  SHA-256 : $sha"
