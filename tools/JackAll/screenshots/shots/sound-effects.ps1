# Replace a sound effect: the Dart Rifle's first-person shot bank, its audio swapped for a test tone
# and a second tone added as a random variation. Nothing is played aloud.
$slug = 'sound-effects'
$inputs = (Get-ShotPaths).Inputs
foreach ($tone in @(@('shot-a.wav', 660), @('shot-b.wav', 880))) {
    & (Get-ShotPaths).Ffmpeg -y -v error -f lavfi -i "sine=f=$($tone[1]):d=0.35,afade=t=out:st=0.2:d=0.15" -ac 1 -ar 44100 -c:a pcm_s16le (Join-Path $inputs $tone[0])
}

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'ext:spk 004bf5e9'
Click-Ui (Select-UiRow '*\004bf5e9.spk' (Find-Ui -Id FileGrid))
$frame = Get-DetailsFrame '004bf5e9.spk'
$tree = Find-Ui -Id SpkTree

Select-Ui (Find-UiByText -Scope $tree -Like 'Play*')
Save-Shot $slug '01-bank' -Callouts $tree, (Find-Ui -Type Text -Name 'Plays'), (Find-Ui -Id SpkAddVariation) -Region $frame

# The audio record's own Export/Import sit below the file's Export button of the same name.
function Get-AudioButton([string]$Label) { @(Find-Ui -Type Button -All | Where-Object { $_.Current.Name -eq $Label.Replace('...', [string][char]0x2026) } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1] }

Select-Ui (Find-UiByText -Scope $tree -Like 'Audio*')
Save-Shot $slug '02-audio' -Callouts (Get-AudioButton 'Export...'), (Get-AudioButton 'Import...') -Region $frame

Invoke-Ui (Get-AudioButton 'Import...')
Complete-FileDialog 'Import replacement audio*' (Join-Path $inputs 'shot-a.wav')
$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\soundbinary\004bf5e9.spk'
Wait-Ui { Test-Path $staged } -What 'the bank to be staged' | Out-Null
$audio = Wait-Ui { Find-UiByText -Scope (Find-Ui -Id SpkTree) -Like 'Audio*0.35 s*' -Timeout 2 } -What 'the new audio'
Save-Shot $slug '03-imported' -Callouts $audio, (Find-Ui -Id SpkStatus) -Region $frame

Select-Ui (Find-UiByText -Scope (Find-Ui -Id SpkTree) -Like 'Play*')
Invoke-Ui (Find-Ui -Id SpkAddVariation)
Complete-FileDialog 'Add variations*' (Join-Path $inputs 'shot-b.wav')
$random = Wait-Ui { Find-UiByText -Scope (Find-Ui -Id SpkTree) -Like 'Random*2 choices*' -Timeout 2 } -What 'the random choice'
Select-Ui $random
Save-Shot $slug '04-variations' -Callouts $random, (Find-Ui -Id SpkChoices), (Find-UiButton 'Remove variation') -Region $frame

Save-ShotResult $slug
Stop-ShotApp
