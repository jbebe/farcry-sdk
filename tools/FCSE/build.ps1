<#
.SYNOPSIS
    Builds FCSE.exe (and the example plugin DLL) with the correct 32-bit toolchain.

.DESCRIPTION
    Wraps the vcvarsall.bat x86 + cmake --preset/--build dance (same pattern as
    tools/misc/modpatcher/build.ps1) so it's a single command instead of something to re-derive
    each time. Must be x86, never x64 - Far Cry 2 is a 32-bit process and neither FCSE.exe nor a
    plugin DLL built for it can load as 64-bit.

    Builds only, by default. Pass -Tests to also run the test suite (ctest) after a successful
    build - it has to be run from here rather than directly, since ctest also only resolves from
    the developer environment this script sets up. Pass -Zip to also package the build for install.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".

.PARAMETER Tests
    Run ctest after building. Off by default.

.PARAMETER Zip
    Package the build into out\fcse-{Config}.zip, laid out so its contents extract straight into
    the game's bin\ folder. Off by default.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Config debug
    .\build.ps1 -Tests
    .\build.ps1 -Zip
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release",

    [switch]$Tests,

    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$Preset = "x86-$Config"
$BuildDir = Join-Path $ProjectRoot "out\build\$Preset"

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $VsWhere)) {
    throw "vswhere.exe not found - is Visual Studio installed?"
}
$VsInstallPath = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsInstallPath) {
    throw "No Visual Studio installation with the C++ x86/x64 build tools component found."
}
$VcVarsAll = Join-Path $VsInstallPath "VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $VcVarsAll)) {
    throw "vcvarsall.bat not found at expected path: $VcVarsAll"
}

Write-Host "Configuring ($Preset)..." -ForegroundColor Cyan
& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$ProjectRoot`" && cmake --preset $Preset"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed (exit $LASTEXITCODE)." }

Write-Host "Building ($Preset)..." -ForegroundColor Cyan
& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cmake --build `"$BuildDir`""
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$OutputExe = Join-Path $BuildDir "FCSE.exe"
Write-Host "Build succeeded: $OutputExe" -ForegroundColor Green

if ($Tests) {
    Write-Host "Testing ($Preset)..." -ForegroundColor Cyan
    & cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$BuildDir`" && ctest --output-on-failure"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
    Write-Host "Tests passed." -ForegroundColor Green
}

if ($Zip) {
    Write-Host "Packaging ($Preset)..." -ForegroundColor Cyan

    # Mirrors the install layout from README.md's "Installing" section, so the zip's contents drop
    # straight into the game's bin\ folder with no rearranging:
    #
    #   FCSE.exe                     next to the untouched FarCry2.exe
    #   plugins\example_plugin.dll   "plugins", not "plugin" - main.cpp scans bin\plugins\ by name
    #
    # The settings-page layout is not here because it is inside FCSE.exe - both .mgb variants are
    # embedded as RCDATA resources at build time (see PLAN-embed-assets.md), so there is no second
    # file to copy, forget, or let go stale against the exe that reads it.
    #
    # Staged to a folder first rather than zipped from the build tree directly: the build directory
    # also holds object files, the test exe, and the plugin's .lib/.exp, none of which ship.
    $StageDir = Join-Path $ProjectRoot "out\package\$Preset"
    $ZipPath = Join-Path $ProjectRoot "out\fcse-$Config.zip"

    # Both rebuilt from scratch, so a file dropped from the layout can't survive in a stale stage
    # folder or get merged into an existing archive.
    if (Test-Path $StageDir) { Remove-Item -Recurse -Force $StageDir }
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "plugins") | Out-Null

    Copy-Item $OutputExe (Join-Path $StageDir "FCSE.exe")
    Copy-Item (Join-Path $BuildDir "example_plugin\example_plugin.dll") `
              (Join-Path $StageDir "plugins\example_plugin.dll")

    Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath
    Write-Host "Packaged: $ZipPath" -ForegroundColor Green
}
