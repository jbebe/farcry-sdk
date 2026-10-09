# Savegames: the list, Purge into a new copy, that copy's values, and deleting the copy again. The
# player's own saves are hashed before and after; only the purged copy is ever written or deleted.
$slug = 'saves'
$folder = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Far Cry 2\Saved Games'
$before = @(Get-TreeManifest $folder @() '.')

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900
Select-UiTab 'Saves'
$grid = Find-Ui -Id SavesGrid
$first = @(Find-Ui -Scope $grid -Type DataItem -All)[0]
Select-Ui $first
$details = Wait-Ui { Find-Ui -Type Text -Name 'Persisted' -Optional } -What 'the save details'
$warning = Find-Ui -Type Text -Like 'Mods and existing saves*'
Save-Shot $slug '01-list' -Callouts $first, $details, $warning

Invoke-Ui (Find-UiButton 'Purge persisted entities...')
$confirm = Wait-Ui { Find-ShotWindow 'Purge persisted entities' } -What 'the purge confirmation'
Save-Shot $slug '02-purge' -Window $confirm -Screen
Push-ShotButton $confirm 'OK'
$status = Wait-ShotStatus 'Wrote *.sav*' -Timeout 120
$copy = [regex]::Match($status, 'Wrote (\d+\.sav)').Groups[1].Value
if (-not $copy) { throw "No purged file name in '$status'" }

$row = Find-UiByText -Scope $grid -Type DataItem -Like "*$copy*"
Select-Ui $row
Wait-Ui { Find-Ui -Type Text -Name $copy -Optional } -What 'the purged copy to be selected' | Out-Null
Invoke-Ui (Find-UiButton 'Open value editor...')
$outline = Wait-Ui { Find-Ui -Id OutlineTree -Optional } -Timeout 120 -What 'the PersistenceDB to open'
$root = @(Find-Ui -Scope $outline -Type TreeItem -All)[0]
Expand-Ui $root
Start-Sleep 2
Select-Ui @(Find-Ui -Scope $outline -Type TreeItem -All)[3]
$note = Wait-Ui { Find-Ui -Type Text -Like 'not an entity placed*' -Optional } -What 'the entity pane'
Save-Shot $slug '03-values' -Callouts $outline, $note
Send-ShotKeys 'Ctrl+W'

Select-UiTab 'Saves'
$row = Find-UiByText -Scope $grid -Type DataItem -Like "*$copy*"
Select-Ui $row
Wait-Ui { Find-Ui -Type Text -Name $copy -Optional } -What 'the purged copy to be selected' | Out-Null
Invoke-Ui (Find-UiButton 'Delete...')
$delete = Wait-Ui { Find-ShotWindow 'Delete save' } -What 'the delete confirmation'
if ((Get-DialogText $delete) -notmatch [regex]::Escape($copy)) { throw "The delete dialog is not for $copy" }
Save-Shot $slug '04-delete' -Window $delete -Screen
Push-ShotButton $delete 'OK'
Wait-Ui { -not (Test-Path (Join-Path $folder $copy)) } -What 'the copy to be deleted' | Out-Null

Stop-ShotApp
$diff = @(Compare-Manifest $before @(Get-TreeManifest $folder @() '.'))
if ($diff.Count) { throw "The saves folder changed: $($diff -join '; ')" }
