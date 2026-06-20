<#
.SYNOPSIS
    Build and publish a Yohaku release to GitHub Releases.

.DESCRIPTION
    Reads the version from src/Yohaku/Yohaku.csproj (single source of truth), checks
    guard rails, builds the self-contained exe via publish.ps1, then (after explicit
    confirmation) creates the GitHub Release with the exe attached and the matching
    CHANGELOG.md section as the body. Nothing is published until you confirm.

    Prerequisites: gh CLI installed and authenticated (gh auth login).

    Before running: bump <Version> in src/Yohaku/Yohaku.csproj and write the matching
    CHANGELOG.md "## [x.y.z]" section, then commit.

.PARAMETER Force
    Skip the confirmation prompt (for non-interactive use). Off by default.

.EXAMPLE
    powershell -File scripts/release.ps1
#>
[CmdletBinding()]
param([switch]$Force)

# ErrorActionPreference is deliberately left as Continue: Stop turns native-command
# stderr (git/gh write status there even on success) into terminating errors on Windows
# PowerShell. Guard rails check exit codes explicitly instead.

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    function Fail([string]$message) {
        Write-Host "release: $message" -ForegroundColor Red
        exit 1
    }

    # --- Version: single source of truth is <Version> in the csproj ---
    $csproj = 'src/Yohaku/Yohaku.csproj'
    $match = [regex]::Match((Get-Content $csproj -Raw -Encoding UTF8), '<Version>(.+?)</Version>')
    if (-not $match.Success) { Fail "could not find <Version> in $csproj" }
    $version = $match.Groups[1].Value
    $tag = "v$version"

    # --- Guard rails: fail before building anything ---
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail 'gh CLI not found; install it, then run: gh auth login'
    }
    gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'gh is not authenticated; run: gh auth login' }

    if (git status --porcelain) { Fail 'working tree is dirty; commit or stash changes first' }

    if (git tag --list $tag) { Fail "tag $tag already exists" }
    gh release view $tag 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Fail "release $tag already exists" }

    # --- Extract this version's CHANGELOG section (everything under "## [x.y.z]") ---
    $notes = & {
        $body = @()
        $inSection = $false
        foreach ($line in (Get-Content 'CHANGELOG.md' -Encoding UTF8)) {
            if ($line -match '^##\s+\[(.+?)\]') {
                if ($inSection) { break }                                      # next version, stop
                if ($Matches[1] -eq $version) { $inSection = $true; continue } # our heading, start
            } elseif ($inSection) {
                $body += $line
            }
        }
        ($body -join "`n").Trim()
    }
    if ([string]::IsNullOrWhiteSpace($notes)) { Fail "no CHANGELOG.md section found for [$version]" }

    # --- Build the distributable exe (publish.ps1 runs the tests and publishes) ---
    # Run as a child process so its internal `exit` can't tear down this script.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { Fail 'publish.ps1 failed' }
    $asset = "dist/Yohaku-$version.exe"
    if (-not (Test-Path $asset)) { Fail "expected $asset not found" }
    $sha    = (Get-FileHash $asset -Algorithm SHA256).Hash
    $sizeMb = [math]::Round((Get-Item $asset).Length / 1MB, 1)
    $releaseBody = "$notes`n`n---`nSHA-256 (Yohaku-$version.exe): ``$sha``"

    # --- Confirm before the outward, irreversible step (tag + publish) ---
    Write-Host ''
    Write-Host 'About to publish a GitHub Release:' -ForegroundColor Yellow
    Write-Host "  Tag / title : $tag  (gh creates the tag at HEAD)"
    Write-Host "  Asset       : Yohaku-$version.exe  ($sizeMb MB)"
    Write-Host "  SHA-256     : $sha"
    Write-Host '  Notes       :'
    $releaseBody -split "`n" | ForEach-Object { Write-Host "      $_" }
    Write-Host ''
    if (-not $Force) {
        if ((Read-Host "Type 'yes' to create and publish this release") -ne 'yes') {
            Write-Host 'Aborted; nothing was published.' -ForegroundColor Yellow
            exit 0
        }
    }

    # --- Publish ---
    $notesFile = New-TemporaryFile
    # UTF-8 without BOM, so the release body has no stray leading character on any PS version.
    [System.IO.File]::WriteAllText($notesFile.FullName, $releaseBody, (New-Object System.Text.UTF8Encoding($false)))
    gh release create $tag $asset --title "Yohaku $tag" --notes-file $notesFile.FullName
    $published = ($LASTEXITCODE -eq 0)
    Remove-Item $notesFile -ErrorAction SilentlyContinue
    if (-not $published) { Fail 'gh release create failed' }
    Write-Host "Published $tag (Yohaku-$version.exe)" -ForegroundColor Green
}
finally {
    Pop-Location
}
