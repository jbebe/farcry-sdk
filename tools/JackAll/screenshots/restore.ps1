# Puts the game folder back the way the last screenshot session found it, e.g. after a crash.
# -Force restores even when patch.dat was changed by something outside the session.
param([switch]$Force)

Import-Module (Join-Path $PSScriptRoot 'JackAllShots.psm1') -Force -DisableNameChecking
Exit-ShotSession -Force:$Force
