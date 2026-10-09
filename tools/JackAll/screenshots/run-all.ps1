# Re-shoots the tutorials' screenshots against the real game folder, which is snapshotted first and
# restored afterwards. -Only takes tutorial slugs; the order in order.psd1 is the dependency order.
param([string[]]$Only, [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
Import-Module (Join-Path $PSScriptRoot 'JackAllShots.psm1') -Force -DisableNameChecking
$order = (Import-PowerShellDataFile (Join-Path $PSScriptRoot 'order.psd1')).Tutorials

Enter-ShotSession
try {
    foreach ($slug in $order) {
        if ($Only -and $slug -notin $Only) { continue }
        Write-Host "== $slug"
        & (Join-Path $PSScriptRoot "shots\$slug.ps1")
    }
}
finally {
    Exit-ShotSession
}
