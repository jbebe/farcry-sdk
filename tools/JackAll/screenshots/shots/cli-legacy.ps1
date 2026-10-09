# Taking features out of a legacy mod from the command line: Skip Intro's analysis, written to
# bin\transcripts. Nothing here writes to the game.
$slug = 'cli-legacy'
$paths = Get-ShotPaths
$game = $paths.Game
$zip = Join-Path (Get-ShotPaths).Repo 'tmp\mods\skip-intro-mod.zip'
$mod = Join-Path (Get-ShotPaths).Repo 'legacy\skip-intro'
$root = Join-Path $paths.Inputs 'legacy'
$work = Join-Path $root 'work\skip-intro'
$picked = Join-Path $root 'picked\skip-opening'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }

Invoke-ShotCli 'legacy', 'analyze', '--game', $game, '--from', $zip, '--work', $work -Check
Invoke-ShotCli 'legacy', 'changes', '--work', $work, '--group' | Out-Null
Invoke-ShotCli 'legacy', 'changes', '--work', $work | Out-Null
Invoke-ShotCli 'legacy', 'check', '--mod', $mod, '--work', $work, '--features' -Check
Invoke-ShotCli 'legacy', 'changes', '--work', $work, '--mod', $mod, '--feature', 'skip-opening-sequence' | Out-Null
Invoke-ShotCli 'legacy', 'pick', '--game', $game, '--mod', $mod, '--work', $work, '--feature', 'skip-opening-sequence', '--out', $picked -Check
Invoke-ShotCli 'mod', 'inspect', $picked, '--game', $game | Out-Null
Save-ShotTranscript $slug
Get-ChildItem -LiteralPath $picked -Recurse -File | ForEach-Object { Write-Host "  picked: $($_.FullName.Substring($picked.Length))" }
