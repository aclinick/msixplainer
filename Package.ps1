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

.PARAMETER NoBump
Skip the automatic version bump.

.PARAMETER BumpRevision
Bump the 4th version segment (revision) instead of the 3rd (build). Useful for
rapid local sideload iterations. NOTE: Microsoft Store requires the 4th segment
to be 0, so don't use this for Store-bound builds.

.EXAMPLE
.\Package.ps1                              # Auto-bumps build segment, full build + bundle
.\Package.ps1 -SkipTests                   # Skip tests
.\Package.ps1 -CertPath .\prod.pfx         # Use existing cert
.\Package.ps1 -SelfContained               # Include WinAppSDK runtime
.\Package.ps1 -BumpRevision                # Bump revision only (sideload only)
.\Package.ps1 -NoBump                      # Repackage same version
#>

param(
    [switch]$SkipTests,
    [string]$CertPath,
    [string]$CertPassword = "password",
    [switch]$SelfContained,
    [switch]$NoBump,
    [switch]$BumpRevision
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

# ── 0.5 Auto-bump manifest version ──

if (-not $NoBump) {
    $manifestContent = [System.IO.File]::ReadAllText($manifest)
    $versionRegex = '(<Identity\b[^>]*\bVersion=")(\d+)\.(\d+)\.(\d+)\.(\d+)(")'
    $match = [regex]::Match($manifestContent, $versionRegex)
    if (-not $match.Success) {
        Write-Host "ERROR: Could not locate Identity Version in $manifest" -ForegroundColor Red
        exit 1
    }
    $major    = [int]$match.Groups[2].Value
    $minor    = [int]$match.Groups[3].Value
    $build    = [int]$match.Groups[4].Value
    $revision = [int]$match.Groups[5].Value
    $oldVer   = "$major.$minor.$build.$revision"

    if ($BumpRevision) {
        $revision++
    } else {
        $build++
        $revision = 0
    }

    $newVer = "$major.$minor.$build.$revision"
    $manifestContent = [regex]::Replace(
        $manifestContent, $versionRegex,
        "`${1}$newVer`${6}", 1)

    # Preserve UTF-8 BOM (WinAppSDK manifest tooling expects it)
    [System.IO.File]::WriteAllText($manifest, $manifestContent, [System.Text.UTF8Encoding]::new($true))

    Write-Host "`n=== Version bumped $oldVer -> $newVer ===" -ForegroundColor Cyan
}

# ── Re-read manifest version so subsequent steps know what to embed in the CLI build ──
$manifestVersion = ([xml](Get-Content $manifest)).Package.Identity.Version

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

$cliProject  = Join-Path $PSScriptRoot "MSIXplainer.Cli\MSIXplainer.Cli.csproj"
$cliProjDir  = Split-Path $cliProject -Parent
$msixFiles   = @()

foreach ($platform in $platforms) {
    $rid = $platform.ToLower()

    Write-Host "`n=== Cleaning bin\$platform\Release (WinUI + CLI) ===" -ForegroundColor DarkGray
    # Wipe stale outputs (old project names, embedded .msix, etc.) before each platform build.
    Remove-Item (Join-Path $projectDir "bin\$platform\Release") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $cliProjDir "bin\$platform\Release") -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "`n=== Building CLI ($platform Release) ===" -ForegroundColor Cyan
    # Publish self-contained so the apphost looks for the runtime alongside itself.
    # We copy only the CLI-specific files; the WinUI publish below supplies the
    # actual .NET 10 runtime DLLs (coreclr.dll, System.*.dll, etc.) in the same
    # package folder, so we don't duplicate the runtime.
    if ($msbuild) {
        & $msbuild $cliProject /nologo /v:m /restore /t:Publish /p:Configuration=Release /p:Platform=$platform /p:RuntimeIdentifier=win-$rid /p:SelfContained=true /p:PublishSingleFile=false /p:PublishTrimmed=false /p:Version=$manifestVersion /p:AssemblyVersion=$manifestVersion /p:FileVersion=$manifestVersion /p:InformationalVersion=$manifestVersion
    } else {
        dotnet publish $cliProject -c Release -p:Platform=$platform -r win-$rid --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:Version=$manifestVersion -p:AssemblyVersion=$manifestVersion -p:FileVersion=$manifestVersion -p:InformationalVersion=$manifestVersion -v m
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: $platform CLI build failed." -ForegroundColor Red
        exit 1
    }

    Write-Host "`n=== Building WinUI ($platform Release) ===" -ForegroundColor Cyan
    if ($msbuild) {
        & $msbuild $project /nologo /v:m /restore /p:Configuration=Release /p:Platform=$platform /p:Version=$manifestVersion /p:AssemblyVersion=$manifestVersion /p:FileVersion=$manifestVersion /p:InformationalVersion=$manifestVersion
    } else {
        dotnet build $project -c Release -p:Platform=$platform -p:Version=$manifestVersion -p:AssemblyVersion=$manifestVersion -p:FileVersion=$manifestVersion -p:InformationalVersion=$manifestVersion -v m
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: $platform WinUI build failed." -ForegroundColor Red
        exit 1
    }

    # Locate WinUI build output (the folder winapp package will pack)
    $binDir = Join-Path $projectDir "bin\$platform\Release"
    $tfmDir = Get-ChildItem $binDir -Directory | Where-Object { $_.Name -match "^net\d" } |
              Sort-Object Name -Descending | Select-Object -First 1
    if (-not $tfmDir) {
        Write-Host "ERROR: No TFM folder found in $binDir" -ForegroundColor Red
        exit 1
    }

    $outputDir = Join-Path $tfmDir.FullName "win-$rid"
    if (-not (Test-Path $outputDir)) { $outputDir = $tfmDir.FullName }

    # Locate CLI publish output and copy required files alongside MSIXplainer.exe.
    # Look for a "publish" subfolder under the TFM/RID layout.
    $cliBinDir = Join-Path $cliProjDir "bin\$platform\Release"
    $cliTfmDir = Get-ChildItem $cliBinDir -Directory -ErrorAction SilentlyContinue |
                 Where-Object { $_.Name -match "^net\d" } |
                 Sort-Object Name -Descending | Select-Object -First 1
    if (-not $cliTfmDir) {
        Write-Host "ERROR: CLI build output not found under $cliBinDir" -ForegroundColor Red
        exit 1
    }
    $cliPublishDir = Join-Path $cliTfmDir.FullName "win-$rid\publish"
    if (-not (Test-Path $cliPublishDir)) {
        # Fallback: publish drops into TFM\RID without a "publish" subfolder when invoked via MSBuild target.
        $cliPublishDir = Join-Path $cliTfmDir.FullName "win-$rid"
    }
    if (-not (Test-Path $cliPublishDir)) {
        Write-Host "ERROR: CLI publish folder not found: expected $cliPublishDir" -ForegroundColor Red
        exit 1
    }

    $cliFiles = @(
        "MSIXplainer.Cli.exe",
        "MSIXplainer.Cli.dll",
        "MSIXplainer.Cli.deps.json",
        "MSIXplainer.Cli.runtimeconfig.json"
    )
    foreach ($f in $cliFiles) {
        $src = Join-Path $cliPublishDir $f
        if (-not (Test-Path $src)) {
            Write-Host "ERROR: Missing CLI artifact: $src" -ForegroundColor Red
            exit 1
        }
        Copy-Item $src -Destination $outputDir -Force
    }
    # Copy every Spectre.Console.*.dll the publish step produced. Spectre split into
    # multiple assemblies (Spectre.Console, Spectre.Console.Ansi, …) so a fixed list
    # silently drops dependencies whenever the package surface changes.
    $spectreDlls = Get-ChildItem -Path $cliPublishDir -Filter "Spectre.Console*.dll" -File -ErrorAction SilentlyContinue
    if (-not $spectreDlls -or $spectreDlls.Count -eq 0) {
        Write-Host "ERROR: No Spectre.Console*.dll files found in $cliPublishDir" -ForegroundColor Red
        exit 1
    }
    foreach ($dll in $spectreDlls) {
        Copy-Item $dll.FullName -Destination $outputDir -Force
    }
    Write-Host "Copied CLI binaries into $outputDir" -ForegroundColor DarkGray

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
