# Builds layer\ from the extracted retail worlds archive: tmpla renamed to newworld_clone, and a
# one-sector cut of it, newworld_tile, with its spawn point lifted onto the ground.
. "$PSScriptRoot\..\..\scripts\tmpla-world.ps1"

if (Test-Path "$PSScriptRoot\layer") { Remove-Item -Recurse -Force "$PSScriptRoot\layer" }
Build-TmplaWorld "$PSScriptRoot\layer\mods" 'newworld_clone' $tmplaSide $null
Build-TmplaWorld "$PSScriptRoot\layer\mods" 'newworld_tile' 1 28, 32, 16.5
Write-Host "Wrote $PSScriptRoot\layer"
