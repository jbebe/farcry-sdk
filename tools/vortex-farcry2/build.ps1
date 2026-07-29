<#
.SYNOPSIS
    Assembles the Vortex extension into dist\ - the folder you drop into Vortex.

.DESCRIPTION
    Two halves get built here: the JavaScript bundle (webpack, from src\), and the JackAll CLI the
    extension shells out to for every piece of Far Cry 2 mod logic. The CLI is published straight
    from tools\JackAll in this repo rather than downloaded, so the extension can never ship against
    a JackAll it wasn't built with.

    The result is self-contained: no .NET runtime needed on the user's machine, no network access at
    install or deploy time.

.PARAMETER SkipCli
    Reuse whatever is already in dist\bin instead of republishing the CLI. Publishing it takes far
    longer than the JS build, so this is what you want while iterating on the extension itself.

.PARAMETER ReadyToRun
    Publish the CLI with ahead-of-time compilation. Roughly doubles the exe (about 20 MB to 40 MB)
    to save a few hundred milliseconds of startup on a process that runs a handful of times per
    session - which is a bad trade for something users have to download, so it's off by default.
#>
[CmdletBinding()]
param(
    [switch]$SkipCli,
    [switch]$ReadyToRun
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$cliProject = Join-Path $root '..\JackAll\src\JackAll.Cli\JackAll.Cli.csproj'

if (-not $SkipCli) {
    Write-Host 'Publishing jackall-cli...' -ForegroundColor Cyan
    $cliOut = Join-Path $dist 'bin'
    if (Test-Path $cliOut) { Remove-Item -Recurse -Force $cliOut }
    dotnet publish $cliProject `
        --configuration Release `
        --runtime win-x64 `
        --output $cliOut `
        -p:PublishReadyToRun=$($ReadyToRun.IsPresent.ToString().ToLower()) `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

if (-not (Test-Path (Join-Path $root 'node_modules'))) {
    Write-Host 'Installing npm dependencies...' -ForegroundColor Cyan
    npm install --no-audit --no-fund --legacy-peer-deps
    if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }
}

Write-Host 'Building the extension bundle...' -ForegroundColor Cyan
npm run build
if ($LASTEXITCODE -ne 0) { throw 'webpack build failed.' }

# Vortex reads info.json to identify the extension and gameart.jpg for the game tile; index.js is
# whatever webpack just wrote. They have to sit at the top of the same folder as bin\.
Copy-Item (Join-Path $root 'info.json') $dist -Force
Copy-Item (Join-Path $root 'gameart.jpg') $dist -Force

$size = (Get-ChildItem $dist -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Extension assembled in {0} ({1:N1} MB)" -f $dist, $size) -ForegroundColor Green
