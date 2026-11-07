<#
.SYNOPSIS
    Builds ThirdPerson.dll with the 32-bit toolchain and stages it in layer\plugins\third-person\.

.DESCRIPTION
    Wraps the vcvarsall.bat x86 + cmake --preset/--build dance, the same way tools/FCSE/build.ps1
    does. Must be x86, never x64 - Far Cry 2 is a 32-bit process and a 64-bit DLL cannot load into
    it. The game gets the plugin with the layer, from jackall-cli mod build together with every
    other layer installed, so that none of theirs is dropped.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Config debug
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$Preset = "x86-$Config"
$BuildDir = Join-Path $ProjectRoot "out\build\$Preset"
$Staged = Join-Path $ProjectRoot "layer\plugins\third-person"

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

New-Item -ItemType Directory -Force -Path $Staged | Out-Null
Copy-Item (Join-Path $BuildDir "ThirdPerson.dll") (Join-Path $Staged "ThirdPerson.dll") -Force
Write-Host "Build succeeded: $Staged" -ForegroundColor Green
