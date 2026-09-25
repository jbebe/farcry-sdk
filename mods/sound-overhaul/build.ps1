<#
.SYNOPSIS
    Builds SoundOverhaul.dll with the 32-bit toolchain, and optionally installs it with DSOAL.

.DESCRIPTION
    Wraps the vcvarsall.bat x86 + cmake --preset/--build dance, the same way tools/FCSE/build.ps1
    does. The built DLL is staged into layer\plugins\sound-overhaul\, beside the bundled DSOAL.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".

.PARAMETER Install
    Path to the game's bin folder (the one holding FarCry2.exe and FCSE.exe). The staged
    layer\plugins\sound-overhaul\ folder is copied to its plugins\sound-overhaul\. Off by default.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Install "C:\Games\Far Cry 2\bin"
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
$Staged = Join-Path $ProjectRoot "layer\plugins\sound-overhaul"

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

if ($Install -and -not (Test-Path (Join-Path $Install "FarCry2.exe"))) {
    throw "$Install does not look like the game's bin folder - no FarCry2.exe in it."
}

Write-Host "Building ($Preset)..." -ForegroundColor Cyan
& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$ProjectRoot`" && cmake --preset $Preset && cmake --build `"$BuildDir`""
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

Copy-Item (Join-Path $BuildDir "SoundOverhaul.dll") (Join-Path $Staged "SoundOverhaul.dll") -Force
Write-Host "Build succeeded: $Staged" -ForegroundColor Green

if ($Install) {
    $PluginDir = Join-Path $Install "plugins\sound-overhaul"
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
    Copy-Item (Join-Path $Staged "*") $PluginDir -Recurse -Force
    Write-Host "Installed: $PluginDir" -ForegroundColor Green

    if (Test-Path (Join-Path $Install "dsound.dll")) {
        Write-Warning "$Install\dsound.dll replaces DirectSound itself; remove it to use the bundled DSOAL."
    }
}
