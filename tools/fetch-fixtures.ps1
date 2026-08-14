<#
.SYNOPSIS
  Download and extract official EELS execution-spec fixtures for Schlieren.

.DESCRIPTION
  Fetches fixtures.tar.gz from ethereum/execution-specs (default: tests@v20.0.1)
  into the repo root and extracts into ./fixtures.

  The archive is intentionally NOT committed (GitHub 100 MB file limit).
  This script is the supported way to provision fixtures on a fresh clone.

.EXAMPLE
  pwsh ./tools/fetch-fixtures.ps1

.EXAMPLE
  pwsh ./tools/fetch-fixtures.ps1 -Force
  # Re-download and re-extract even if fixtures/ already exists
#>
[CmdletBinding()]
param(
    [string] $Tag = "tests@v20.0.1",
    [string] $Repo = "ethereum/execution-specs",
    [string] $AssetName = "fixtures.tar.gz",
    [string] $RepoRoot = "",
    [switch] $Force,
    [switch] $SkipDownload,
    [switch] $SkipExtract
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$archivePath = Join-Path $RepoRoot $AssetName
$fixturesDir = Join-Path $RepoRoot "fixtures"
# GitHub release tags with '@' are percent-encoded in the download URL.
$encodedTag = [uri]::EscapeDataString($Tag)
$downloadUrl = "https://github.com/$Repo/releases/download/$encodedTag/$AssetName"

function Write-Step([string] $msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Test-HasFixtureJson([string] $dir) {
    if (-not (Test-Path $dir)) { return $false }
    $probe = Get-ChildItem -Path $dir -Filter "*.json" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    return $null -ne $probe
}

Write-Host "Schlieren fixture setup" -ForegroundColor Green
Write-Host "  Repo root : $RepoRoot"
Write-Host "  Release   : $Repo @ $Tag"
Write-Host "  Asset     : $AssetName"
Write-Host "  Extract to: $fixturesDir"
Write-Host ""

if ((Test-HasFixtureJson $fixturesDir) -and -not $Force) {
    Write-Host "fixtures/ already contains JSON files. Skipping download/extract." -ForegroundColor Yellow
    Write-Host "  Pass -Force to re-download and re-extract."
    Write-Host ""
} else {
    if (-not $SkipDownload) {
        if ((Test-Path $archivePath) -and -not $Force) {
            $sizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)
            Write-Step "Using existing archive ($sizeMb MB): $archivePath"
        } else {
            Write-Step "Downloading $downloadUrl"
            Write-Host "  This is ~400 MB; may take a few minutes."
            $tmp = "$archivePath.partial"
            if (Test-Path $tmp) { Remove-Item $tmp -Force }

            # Prefer gh when available (handles auth/rate limits better); fall back to HTTPS.
            $gh = Get-Command gh -ErrorAction SilentlyContinue
            if ($gh) {
                & gh release download $Tag --repo $Repo --pattern $AssetName --dir $RepoRoot --clobber
                if ($LASTEXITCODE -ne 0) {
                    throw "gh release download failed (exit $LASTEXITCODE). Try: gh auth login"
                }
            } else {
                # .NET HttpClient streams to disk without loading the full body into memory.
                Add-Type -AssemblyName System.Net.Http
                $handler = [System.Net.Http.HttpClientHandler]::new()
                $client = [System.Net.Http.HttpClient]::new($handler)
                $client.Timeout = [TimeSpan]::FromHours(2)
                $client.DefaultRequestHeaders.UserAgent.ParseAdd("Schlieren-fetch-fixtures/1.0")
                try {
                    $response = $client.GetAsync($downloadUrl, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                    if (-not $response.IsSuccessStatusCode) {
                        throw "Download failed: HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)"
                    }
                    $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    $fs = [System.IO.File]::Create($tmp)
                    try {
                        $stream.CopyTo($fs)
                    } finally {
                        $fs.Dispose()
                        $stream.Dispose()
                        $response.Dispose()
                    }
                } finally {
                    $client.Dispose()
                    $handler.Dispose()
                }
                Move-Item -Path $tmp -Destination $archivePath -Force
            }

            if (-not (Test-Path $archivePath)) {
                throw "Archive not found after download: $archivePath"
            }
            $sizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)
            Write-Host "  Saved $sizeMb MB -> $archivePath"
        }
    } elseif (-not (Test-Path $archivePath)) {
        throw "-SkipDownload set but archive missing: $archivePath"
    }

    if (-not $SkipExtract) {
        if (-not (Test-Path $archivePath)) {
            throw "Cannot extract; archive missing: $archivePath"
        }

        Write-Step "Extracting archive into $RepoRoot"
        # Official asset unpacks a top-level "fixtures/" directory.
        # Use Windows built-in tar (bsdtar).
        $tar = Get-Command tar -ErrorAction SilentlyContinue
        if (-not $tar) {
            throw "tar not found. Install Windows tar or extract $archivePath manually."
        }

        if ((Test-Path $fixturesDir) -and $Force) {
            Write-Host "  Removing existing fixtures/ (-Force)"
            Remove-Item -LiteralPath $fixturesDir -Recurse -Force
        }

        Push-Location $RepoRoot
        try {
            & tar -xzf $AssetName
            if ($LASTEXITCODE -ne 0) {
                throw "tar extract failed (exit $LASTEXITCODE)"
            }
        } finally {
            Pop-Location
        }

        if (-not (Test-HasFixtureJson $fixturesDir)) {
            throw "Extract finished but no fixture JSON found under $fixturesDir"
        }
        Write-Host "  Extract complete."
    }
}

$stateRoot = Join-Path $fixturesDir "state_tests"
$jsonCount = 0
if (Test-Path $fixturesDir) {
    $jsonCount = @(Get-ChildItem -Path $fixturesDir -Filter "*.json" -Recurse -ErrorAction SilentlyContinue).Count
}

Write-Host ""
Write-Host "Ready." -ForegroundColor Green
Write-Host "  fixtures JSON files : $jsonCount"
if (Test-Path $stateRoot) {
    Write-Host "  state_tests root    : $stateRoot"
}
Write-Host ""
Write-Host "Example harness env (PowerShell):" -ForegroundColor Cyan
Write-Host "  `$env:EELS_FIXTURES_ROOT = `"$($stateRoot -replace '\\','/')`""
Write-Host "  `$env:EELS_INCLUDE_SUBDIRS = `"1`""
Write-Host "  `$env:EELS_REQUIRED_FORK = `"Osaka`""
Write-Host "  `$env:EELS_MAX_CASES = `"9999`""
Write-Host ""
Write-Host "  dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --nologo"
Write-Host ""
Write-Host "Note: fixtures/ and $AssetName stay local (gitignored). Do not commit them."
