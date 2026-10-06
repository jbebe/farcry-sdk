<#
.SYNOPSIS
    Builds AimingOverhaul.dll with the 32-bit toolchain, and optionally installs it and the data
    layer into the game.

.DESCRIPTION
    Builds with the x86 toolchain and stages the DLL into layer\plugins\aiming-overhaul\, where
    jackall-cli deploys it from.

.PARAMETER Config
    "release" or "debug" - selects the x86-release/x86-debug CMake preset. Defaults to "release".

.PARAMETER Install
    Path to the game's bin folder (the one holding FarCry2.exe and FCSE.exe). The layer, plugin
    included, is then built into the game's patch with jackall-cli, which needs a Release build of
    tools\JackAll. That patch holds the vanilla files plus this layer alone, so any other layer
    built into it before is dropped. Off by default.

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
$Layer = Join-Path $ProjectRoot "layer"
$Staged = Join-Path $Layer "plugins\aiming-overhaul"
$JackAll = Join-Path $ProjectRoot "..\..\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"

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

if ($Install) {
    if (-not (Test-Path (Join-Path $Install "FarCry2.exe"))) {
        throw "$Install does not look like the game's bin folder - no FarCry2.exe in it."
    }
    if (-not (Test-Path $JackAll)) {
        throw "jackall-cli.exe not found at $JackAll - build tools\JackAll in Release first."
    }
}

Write-Host "Building ($Preset)..." -ForegroundColor Cyan
& cmd /c "`"$VcVarsAll`" x86 >nul 2>nul && cd /d `"$ProjectRoot`" && cmake --preset $Preset && cmake --build `"$BuildDir`""
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

New-Item -ItemType Directory -Force -Path $Staged | Out-Null
Copy-Item (Join-Path $BuildDir "AimingOverhaul.dll") (Join-Path $Staged "AimingOverhaul.dll") -Force
Write-Host "Build succeeded: $Staged" -ForegroundColor Green

if ($Install) {
    Write-Host "Building the layer into the game..." -ForegroundColor Cyan
    # jackall-cli reports progress on stderr, which Windows PowerShell turns into a terminating
    # error under "Stop" whenever the output is redirected. The exit code is the real verdict.
    $ErrorActionPreference = "Continue"
    & $JackAll mod build --game (Split-Path $Install -Parent) --layer $Layer 2>&1 | ForEach-Object { Write-Host "$_" }
    $ErrorActionPreference = "Stop"
    if ($LASTEXITCODE -ne 0) { throw "jackall-cli mod build failed (exit $LASTEXITCODE)." }
    Write-Host "Installed the layer: $Layer" -ForegroundColor Green
}
