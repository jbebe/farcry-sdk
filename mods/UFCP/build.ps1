<#
.SYNOPSIS
    Builds UFCP.dll with the correct 32-bit toolchain, and optionally installs it into the game.

.DESCRIPTION
    Wraps the vcvarsall.bat x86 + cmake --preset/--build dance, the same way tools/FCSE/build.ps1
    does. Must be x86, never x64 - Far Cry 2 is a 32-bit process and a 64-bit DLL cannot load into
    it.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".

.PARAMETER Install
    Path to the game's bin folder (the one holding FarCry2.exe and FCSE.exe). The built DLL is
    copied into its plugins\ subfolder, which is created if missing. Off by default.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Config debug
    .\build.ps1 -Install "C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin"
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release",

    [string]$Install
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

$OutputDll = Join-Path $BuildDir "UFCP.dll"
Write-Host "Build succeeded: $OutputDll" -ForegroundColor Green

if ($Install) {
    # FCSE scans bin\plugins\ by name, so that is the only place this can go. It creates the folder
    # itself on first run, but installing before ever launching FCSE is the normal order.
    if (-not (Test-Path (Join-Path $Install "FarCry2.exe"))) {
        throw "$Install does not look like the game's bin folder - no FarCry2.exe in it."
    }
    $PluginDir = Join-Path $Install "plugins"
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
    Copy-Item $OutputDll (Join-Path $PluginDir "UFCP.dll") -Force
    Write-Host "Installed: $(Join-Path $PluginDir 'UFCP.dll')" -ForegroundColor Green
}
