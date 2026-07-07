<#
.SYNOPSIS
    Build a distributable Yohaku release archive.

.DESCRIPTION
    Publishes a self-contained, single-file, compressed win-x64 build and packages it as
    dist/yohaku-<version>-win-x64.zip (version read from src/Yohaku/Yohaku.csproj, the
    single source of truth). The archive holds yohaku.exe (stable name), LICENSE.txt, and
    README.txt. Prints the zip's SHA-256 so a release can advertise it. Runs the unit
    tests first unless -SkipTests.

    The exe needs no .NET runtime installed; extract the archive and run yohaku.exe.

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

$built = Join-Path $publishDir 'yohaku.exe'
if (-not (Test-Path $built)) { Fail "expected $built not found after publish" }

# Stage the archive contents: a stable-named exe plus the licence and readme.
$stageDir = Join-Path $repoRoot 'src\Yohaku\bin\publish-stage'
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item $built (Join-Path $stageDir 'yohaku.exe') -Force
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stageDir 'LICENSE.txt') -Force
Copy-Item (Join-Path $repoRoot 'packaging\README.txt') (Join-Path $stageDir 'README.txt') -Force

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$asset = Join-Path $distDir "yohaku-$version-win-x64.zip"
# The trailing \* keeps the files at the archive root rather than nested under a folder.
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $asset -Force

$sha    = (Get-FileHash $asset -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item $asset).Length / 1MB, 1)

Write-Host ''
Write-Host "Built: $asset" -ForegroundColor Green
Write-Host "  Size    : $sizeMb MB"
Write-Host "  SHA-256 : $sha"
