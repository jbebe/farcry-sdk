# Sharing a mod: the workspace from the first mod, zipped and imported back as a test.
$slug = 'sharing-a-mod'
$zip = New-ShotZip 'bigger-ak47-mags.zip' (Join-Path (Get-ShotPaths).Bin 'results\first-mod\workspace')

Reset-ShotState -Workspace 'first-mod'
Start-ShotApp | Out-Null
$grid = Find-Ui -Id ModGrid
$workspace = Find-UiByText -Scope $grid -Type DataItem -Like 'workspace*'
Select-Ui $workspace
Save-Shot $slug '01-open-location' -Callouts $workspace, (Find-Ui -Type Button -Name 'Open location')

Invoke-Ui (Find-Ui -Type Button -Name 'Import mod')
Complete-FileDialog 'Add a mod' $zip
$row = Find-UiByText -Scope $grid -Type DataItem -Like 'bigger-ak47-mags*'
$workspace = Find-UiByText -Scope $grid -Type DataItem -Like 'workspace*'
Set-UiToggle (Find-Ui -Scope $workspace -Type CheckBox) $false
Select-Ui $row
Start-Sleep 1
$deploy = Find-Ui -Type Button -Name 'Deploy mods'
Invoke-Ui $deploy
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-Shot $slug '02-test' -Callouts $row, (Find-Ui -Scope $workspace -Type CheckBox), (Find-Ui -Id ModFileTree), (Get-ShotStatusElement)

Stop-ShotApp
