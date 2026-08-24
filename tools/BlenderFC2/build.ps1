<#
.SYNOPSIS
    Builds the installable Blender extension zip for the Far Cry 2 add-on.

.DESCRIPTION
    Blender builds its own extension zips, and it has to. An extension needs register/unregister at
    the zip root beside the manifest - which is what the root __init__.py re-exports - and the
    manifest's [build] section is what keeps tests\ and the command-line scripts out of the package.
    Zipping the folder by hand produces something Blender will not install.

    Blender validates the manifest on the way through, so a bad version or an unknown tag fails here
    rather than at install time.

    The result is farcry2_formats-<version>.zip, installed through
    Edit > Preferences > Get Extensions > Install from Disk.

.PARAMETER Blender
    Path to blender.exe. Defaults to $env:BLENDER, then blender.exe on PATH, then the newest install
    found under the usual roots.

.PARAMETER OutputDir
    Where to write the zip. Defaults to this folder - the manifest excludes *.zip, so one left here
    never ends up inside the next build.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Blender "C:\Programs\Blender 5.2\blender.exe"
#>
[CmdletBinding()]
param(
    [string]$Blender,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Find-Blender {
    if ($env:BLENDER -and (Test-Path $env:BLENDER)) {
        return $env:BLENDER
    }
    $onPath = Get-Command blender.exe -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }
    # Newest first, so a machine with several Blenders builds against the current one.
    $roots = @(
        'C:\Programs',
        (Join-Path $env:ProgramFiles 'Blender Foundation'),
        (Join-Path ${env:ProgramFiles(x86)} 'Blender Foundation')
    )
    foreach ($candidate in $roots) {
        if (-not $candidate -or -not (Test-Path $candidate)) { continue }
        $found = Get-ChildItem -Path $candidate -Filter 'blender.exe' -Recurse -Depth 2 -ErrorAction SilentlyContinue |
            Sort-Object -Property FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

if (-not $Blender) {
    $Blender = Find-Blender
}
if (-not $Blender -or -not (Test-Path $Blender)) {
    throw 'Could not find blender.exe. Pass -Blender <path>, or set $env:BLENDER.'
}

if (-not $OutputDir) {
    $OutputDir = $root
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}
$OutputDir = (Resolve-Path $OutputDir).Path

$manifest = Join-Path $root 'blender_manifest.toml'
if (-not (Test-Path $manifest)) {
    throw "No blender_manifest.toml in $root; this is not an extension folder."
}
$manifestText = Get-Content $manifest -Raw
$id = [regex]::Match($manifestText, '(?m)^\s*id\s*=\s*"([^"]+)"').Groups[1].Value
$version = [regex]::Match($manifestText, '(?m)^\s*version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $id -or -not $version) {
    throw "Could not read id and version out of $manifest."
}

$zipPath = Join-Path $OutputDir "$id-$version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Building $id $version with $Blender..." -ForegroundColor Cyan
& $Blender --command extension build --source-dir $root --output-dir $OutputDir
if ($LASTEXITCODE -ne 0) { throw 'blender extension build failed.' }
if (-not (Test-Path $zipPath)) {
    throw "Blender reported success but $zipPath is not there."
}

# Validating the built zip rather than the source folder, so what is checked is what ships.
Write-Host 'Validating the built extension...' -ForegroundColor Cyan
& $Blender --command extension validate $zipPath
if ($LASTEXITCODE -ne 0) { throw "blender rejected $zipPath." }

$size = (Get-Item $zipPath).Length / 1KB
Write-Host ("Built {0} ({1:N0} KB)" -f $zipPath, $size) -ForegroundColor Green
Write-Host 'Install with Edit > Preferences > Get Extensions > Install from Disk.' -ForegroundColor Green
