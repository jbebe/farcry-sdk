# Taking features out of a legacy mod from the command line: Skip Intro's analysis, written to
# bin\transcripts. Nothing here writes to the game.
$slug = 'cli-legacy'
$paths = Get-ShotPaths
$game = $paths.Game
$zip = 'C:\Projects\FarCry2\tmp\mods\skip-intro-mod.zip'
$mod = 'C:\Projects\FarCry2\legacy\skip-intro'
$root = Join-Path $paths.Bin 'inputs\legacy'
$work = Join-Path $root 'work\skip-intro'
$picked = Join-Path $root 'picked\skip-opening'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }

if (Invoke-ShotCli 'legacy', 'analyze', '--game', $game, '--from', $zip, '--work', $work) { throw 'The analysis failed' }
Invoke-ShotCli 'legacy', 'changes', '--work', $work, '--group' | Out-Null
Invoke-ShotCli 'legacy', 'changes', '--work', $work | Out-Null
if (Invoke-ShotCli 'legacy', 'check', '--mod', $mod, '--work', $work, '--features') { throw 'The check is not clean' }
Invoke-ShotCli 'legacy', 'changes', '--work', $work, '--mod', $mod, '--feature', 'skip-opening-sequence' | Out-Null
if (Invoke-ShotCli 'legacy', 'pick', '--game', $game, '--mod', $mod, '--work', $work, '--feature', 'skip-opening-sequence', '--out', $picked) { throw 'The pick failed' }
Invoke-ShotCli 'mod', 'inspect', $picked, '--game', $game | Out-Null
Save-ShotTranscript $slug
Get-ChildItem -LiteralPath $picked -Recurse -File | ForEach-Object { Write-Host "  picked: $($_.FullName.Substring($picked.Length))" }
