<#
.SYNOPSIS
    Builds MapOverhaul.dll with the 32-bit toolchain and stages it in layer\plugins\map-overhaul\.

.DESCRIPTION
    The game gets it with the layer, from jackall-cli mod build together with every other layer
    installed, so that none of theirs is dropped.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$Preset = "x86-$Config"
$BuildDir = Join-Path $ProjectRoot "out\build\$Preset"
$Staged = Join-Path $ProjectRoot "layer\plugins\map-overhaul"

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $VsWhere)) {
    throw "vswhere.exe not found - is Visual Studio installed?"
}
$VsInstallPath = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsInstallPath) {
    throw "No Visual Studio installation with the C++ x86/x64 build tools component found."
}
$VcVarsAll = Join-Path $VsInstallPath "VC\Auxiliary\Build\vcvarsall.bat"

Write-Host "Building ($Preset)..." -ForegroundColor Cyan
& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$ProjectRoot`" && cmake --preset $Preset && cmake --build `"$BuildDir`""
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

New-Item -ItemType Directory -Force -Path $Staged | Out-Null
Copy-Item (Join-Path $BuildDir "MapOverhaul.dll") (Join-Path $Staged "MapOverhaul.dll") -Force
Write-Host "Build succeeded: $Staged" -ForegroundColor Green
