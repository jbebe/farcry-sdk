# Managing mods from the command line: transcripts of the mod commands, written to bin\transcripts.
$slug = 'cli-mods'
$paths = Get-ShotPaths
$inputs = Join-Path $paths.Bin 'inputs'
$game = $paths.Game
$vss = 'C:\Projects\FarCry2\mods\vss-vintorez\vss-vintorez-1.1.0.zip'
$flashlight = Join-Path $inputs 'flashlight-1.1.1.zip'
$skip = 'C:\Projects\FarCry2\tmp\mods\FC2 Skip Intro-320-1-1-1651917084.zip'
$magazine = Join-Path $paths.Bin 'results\first-mod\workspace'

Invoke-ShotCli 'mod', 'status', '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $vss, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $flashlight, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $skip, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'lint', '--game', $game, '--layer', $magazine | Out-Null
if (Invoke-ShotCli 'mod', 'build', '--game', $game, '--layer', $vss, '--layer', $flashlight, '--layer', $magazine) { throw 'The build failed' }
Add-SessionPatch
Invoke-ShotCli 'mod', 'build', '--game', $game, '--layer', (Join-Path $inputs 'ak47-40-rounds.zip'), '--layer', (Join-Path $inputs 'ak47-50-rounds.zip') | Out-Null
Add-SessionPatch
if (Invoke-ShotCli 'mod', 'restore', '--game', $game) { throw 'The restore failed' }
Add-SessionPatch
Invoke-ShotCli 'mod', 'status', '--game', $game, '--json' | Out-Null
Save-ShotTranscript $slug
