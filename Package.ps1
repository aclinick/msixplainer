<#
.SYNOPSIS
Builds MSIXplainer for x64 and ARM64, creates individual MSIX packages, and bundles them into an .msixbundle.

.DESCRIPTION
One command to produce a distributable .msixbundle:

  .\Package.ps1

Steps:
  1. Runs tests to ensure nothing is broken
  2. Builds Release for x64 and ARM64
  3. Generates a dev certificate (if devcert.pfx doesn't exist)
  4. Creates per-arch .msix packages via winapp package
  5. Bundles into a signed .msixbundle via makeappx

Output lands in the .\artifacts\ folder.

.PARAMETER SkipTests
Skip running tests before building.

.PARAMETER CertPath
Path to an existing .pfx certificate. Default: generates devcert.pfx.

.PARAMETER CertPassword
Password for the certificate. Default: password.

.PARAMETER SelfContained
Bundle Windows App SDK runtime (larger but no runtime dependency).

.EXAMPLE
.\Package.ps1                              # Full build + bundle
.\Package.ps1 -SkipTests                   # Skip tests
.\Package.ps1 -CertPath .\prod.pfx         # Use existing cert
.\Package.ps1 -SelfContained               # Include WinAppSDK runtime
#>

param(
    [switch]$SkipTests,
    [string]$CertPath,
    [string]$CertPassword = "password",
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$projectDir   = Join-Path $PSScriptRoot "MSIXplainer"
$project      = Join-Path $projectDir "MSIXplainer.csproj"
$manifest     = Join-Path $projectDir "Package.appxmanifest"
$artifactsDir = Join-Path $PSScriptRoot "artifacts"
$platforms    = @("x64", "ARM64")

# ── 0. Prerequisites ──

$winapp = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $winapp) {
    Write-Host "ERROR: winapp CLI not found in PATH." -ForegroundColor Red
    Write-Host "Install: dotnet tool install -g Microsoft.Windows.Dev.CLI" -ForegroundColor Yellow
    exit 1
}

# ── 1. Run tests ──

if (-not $SkipTests) {
    Write-Host "`n=== Running tests ===" -ForegroundColor Cyan
    $testProj = Join-Path $PSScriptRoot "MSIXplainer.Core.Tests"
    & dotnet test $testProj --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nTests failed - aborting packaging." -ForegroundColor Red
        exit 1
    }
    Write-Host "Tests passed." -ForegroundColor Green
}

# ── 2. Clean artifacts ──

if (Test-Path $artifactsDir) { Remove-Item $artifactsDir -Recurse -Force }
New-Item $artifactsDir -ItemType Directory -Force | Out-Null

# ── 3. Certificate ──

if ($CertPath) {
    if (-not (Test-Path $CertPath)) {
        Write-Host "ERROR: Certificate not found: $CertPath" -ForegroundColor Red
        exit 1
    }
    $CertPath = (Resolve-Path $CertPath).Path
} else {
    $CertPath = Join-Path $PSScriptRoot "devcert.pfx"
    if (-not (Test-Path $CertPath)) {
        Write-Host "`n=== Generating development certificate ===" -ForegroundColor Cyan
        & winapp cert generate --manifest $manifest --if-exists skip
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Certificate generation failed." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "`n=== Using existing devcert.pfx ===" -ForegroundColor DarkGray
    }
}

# ── 4. Find MSBuild ──

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}

# ── 5. Build + Package each architecture ──

$msixFiles = @()

foreach ($platform in $platforms) {
    Write-Host "`n=== Building $platform Release ===" -ForegroundColor Cyan

    $rid = $platform.ToLower()

    if ($msbuild) {
        & $msbuild $project /nologo /v:m /restore /p:Configuration=Release /p:Platform=$platform
    } else {
        dotnet build $project -c Release -p:Platform=$platform -v m
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: $platform build failed." -ForegroundColor Red
        exit 1
    }

    # Locate build output
    $binDir = Join-Path $projectDir "bin\$platform\Release"
    $tfmDir = Get-ChildItem $binDir -Directory | Where-Object { $_.Name -match "^net\d" } |
              Sort-Object Name -Descending | Select-Object -First 1
    if (-not $tfmDir) {
        Write-Host "ERROR: No TFM folder found in $binDir" -ForegroundColor Red
        exit 1
    }

    $outputDir = Join-Path $tfmDir.FullName "win-$rid"
    if (-not (Test-Path $outputDir)) { $outputDir = $tfmDir.FullName }

    Write-Host "`n=== Packaging $platform ===" -ForegroundColor Cyan

    $msixName = "MSIXplainer_$rid.msix"
    $msixPath = Join-Path $artifactsDir $msixName
    $packageArgs = @($outputDir, "--manifest", $manifest, "--output", $msixPath, "--cert", $CertPath, "--cert-password", $CertPassword)
    if ($SelfContained) { $packageArgs += "--self-contained" }

    & winapp package @packageArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: $platform packaging failed." -ForegroundColor Red
        exit 1
    }

    $msixFiles += $msixPath
    Write-Host "Created: $msixName" -ForegroundColor Green
}

# ── 6. Bundle ──

Write-Host "`n=== Creating .msixbundle ===" -ForegroundColor Cyan

# Read version from manifest
[xml]$manifestXml = Get-Content $manifest
$version = $manifestXml.Package.Identity.Version

$bundleName = "MSIXplainer_${version}.msixbundle"
$bundlePath = Join-Path $artifactsDir $bundleName

# makeappx bundle needs a flat directory of .msix files
& winapp tool makeappx bundle /d $artifactsDir /p $bundlePath
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Bundle creation failed." -ForegroundColor Red
    exit 1
}

# ── 7. Sign the bundle ──

Write-Host "`n=== Signing bundle ===" -ForegroundColor Cyan
& winapp sign $bundlePath $CertPath --password $CertPassword
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Bundle signing failed." -ForegroundColor Red
    exit 1
}

# ── 8. Clean up individual .msix files (bundle contains them) ──

foreach ($f in $msixFiles) {
    Remove-Item $f -ErrorAction SilentlyContinue
}

# ── Done ──

$size = [math]::Round((Get-Item $bundlePath).Length / 1MB, 1)
Write-Host "`n=== Packaging complete ===" -ForegroundColor Green
Write-Host "  Bundle: $bundlePath `($size MB`)" -ForegroundColor White
Write-Host "  Cert:   $CertPath" -ForegroundColor White
Write-Host ""
Write-Host "To install, trust the certificate first:" -ForegroundColor Yellow
Write-Host "  winapp cert install $CertPath   # requires admin" -ForegroundColor White
Write-Host "  Add-AppxPackage $bundlePath" -ForegroundColor White
