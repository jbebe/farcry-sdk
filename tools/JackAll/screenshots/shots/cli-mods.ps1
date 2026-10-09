# Managing mods from the command line: transcripts of the mod commands, written to bin\transcripts.
$slug = 'cli-mods'
$paths = Get-ShotPaths
$inputs = $paths.Inputs
$game = $paths.Game
$vss = Join-Path (Get-ShotPaths).Repo 'mods\vss-vintorez\vss-vintorez-1.1.0.zip'
$flashlight = Join-Path $inputs 'flashlight-1.1.1.zip'
$skip = Join-Path (Get-ShotPaths).Repo 'tmp\mods\FC2 Skip Intro-320-1-1-1651917084.zip'
$magazine = Join-Path $paths.Bin 'results\first-mod\workspace'

Invoke-ShotCli 'mod', 'status', '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $vss, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $flashlight, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'inspect', $skip, '--game', $game | Out-Null
Invoke-ShotCli 'mod', 'lint', '--game', $game, '--layer', $magazine | Out-Null
Invoke-ShotCli 'mod', 'build', '--game', $game, '--layer', $vss, '--layer', $flashlight, '--layer', $magazine -Check
Add-SessionPatch
Invoke-ShotCli 'mod', 'build', '--game', $game, '--layer', (Join-Path $inputs 'ak47-40-rounds.zip'), '--layer', (Join-Path $inputs 'ak47-50-rounds.zip') | Out-Null
Add-SessionPatch
Invoke-ShotCli 'mod', 'restore', '--game', $game -Check
Add-SessionPatch
Invoke-ShotCli 'mod', 'status', '--game', $game, '--json' | Out-Null
Save-ShotTranscript $slug
