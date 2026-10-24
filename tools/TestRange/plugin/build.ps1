<#
.SYNOPSIS
    Builds TestRange.dll, the test range's scenarios, with the 32-bit toolchain.

.DESCRIPTION
    Wraps the vcvarsall.bat x86 + cmake --preset/--build dance the other plugins use. The DLL lands in
    out\build\<preset>\; ..\build-layer.ps1 packs it into the range's layer.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release"
)

$ErrorActionPreference = "Stop"

$Preset = "x86-$Config"
$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsInstallPath = & $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsInstallPath) {
    throw "No Visual Studio installation with the C++ x86/x64 build tools component found."
}
$VcVarsAll = Join-Path $VsInstallPath "VC\Auxiliary\Build\vcvarsall.bat"

& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$PSScriptRoot`" && cmake --preset $Preset && cmake --build `"$PSScriptRoot\out\build\$Preset`""
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
Write-Host "Build succeeded: $PSScriptRoot\out\build\$Preset\TestRange.dll" -ForegroundColor Green
