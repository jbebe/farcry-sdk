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
    Package the build into out\fcse-{Config}.zip and out\fcse-plugins-{Config}.zip - the loader and
    the example plugin/script separately - each laid out so its contents extract straight into the
    game's bin\ folder. Off by default.

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

    # Two archives, so installing the loader never also installs a plugin. Contents of each extract
    # straight into the game's bin\ - "plugins", not "plugin", is the folder main.cpp scans by name:
    #
    #   fcse-{Config}.zip          FCSE.exe
    #   fcse-plugins-{Config}.zip  plugins\example_plugin.dll, plugins\example_script.lua
    #
    # Staged to folders, rebuilt from scratch, so the build tree's object files, test exe and
    # .lib/.exp stay out and nothing stale survives into an archive.
    $StageRoot = Join-Path $ProjectRoot "out\package\$Preset"
    if (Test-Path $StageRoot) { Remove-Item -Recurse -Force $StageRoot }

    $ExeStage = Join-Path $StageRoot "fcse"
    $PluginStage = Join-Path $StageRoot "example-plugins\plugins"
    New-Item -ItemType Directory -Force -Path $ExeStage, $PluginStage | Out-Null

    Copy-Item $OutputExe $ExeStage
    Copy-Item (Join-Path $BuildDir "example_plugin\example_plugin.dll") $PluginStage

    # Copied from source, not the build tree: a script is not compiled, and the runtime it calls into
    # is already inside FCSE.exe as a resource.
    Copy-Item (Join-Path $ProjectRoot "example_script\example_script.lua") $PluginStage

    foreach ($Name in "fcse", "example-plugins") {
        $ZipPath = Join-Path $ProjectRoot "out\$Name-$Config.zip"
        if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
        Compress-Archive -Path (Join-Path $StageRoot "$Name\*") -DestinationPath $ZipPath
        Write-Host "Packaged: $ZipPath" -ForegroundColor Green
    }
}
