# The in-game round: deploys a batch of the tutorials' examples into the real game, behind the same
# snapshot as a screenshot run, and prints what to look for. Nothing starts the game. When you are
# done playing, restore.ps1 puts the game folder back as it was.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'JackAllShots.psm1') -Force -DisableNameChecking
$paths = Get-ShotPaths

# Each is a tutorial's saved workspace; run-all.ps1 -Only <slug> makes a missing one.
$examples = 'music', 'legacy-import', 'renaming', 'animations'
$build = @('mod', 'build', '--game', $paths.Game)
foreach ($slug in $examples) {
    $workspace = Join-Path $paths.Bin "results\$slug\workspace"
    if (-not (Test-Path $workspace)) { throw "No result for '$slug' - run run-all.ps1 -Only $slug first." }
    $build += '--layer', $workspace
}

Enter-ShotSession
try {
    Invoke-ShotCli $build -Check -Quiet
    Add-SessionPatch
}
catch {
    Exit-ShotSession
    throw
}

Write-Host @'

Deployed, with no FCSE plugins. Start the game the usual way, in English, and check:

1. Main menu: instead of the theme, a steady three-note chord plays for 30 seconds.
2. New game: it starts in the hotel room in Pala, without the taxi ride. The Jackal's briefing is
   cut short and the wake-up after it is shorter.
3. Pick up an AK-47 (the soldiers in the town escape carry them): the HUD calls it "AK-47 Drum",
   and a full magazine holds 60 rounds.
4. Reload the AK-47 in first person: the animation plays a quarter faster than before.

Skip Intro also raises a mouse filter limit in five input maps, so mouse look may feel different.

When you're done, close the game and run restore.ps1 to put your own mods back.
'@
