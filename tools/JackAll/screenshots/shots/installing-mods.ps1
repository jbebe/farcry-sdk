# Installing mods: import a mod zip, deploy, a plugin mod, revert.
$slug = 'installing-mods'
$vss = 'C:\Projects\FarCry2\mods\vss-vintorez\vss-vintorez-1.1.0.zip'
$flashlight = New-ShotZip 'flashlight-1.1.1.zip' 'C:\Projects\FarCry2\mods\flashlight\layer'

Reset-ShotState
Start-ShotApp | Out-Null

$import = Find-Ui -Type Button -Name 'Import mod'
Invoke-Ui $import
Complete-FileDialog 'Add a mod' $vss
$row = Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'vss-vintorez*'
Select-Ui $row
$workspace = Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'workspace*'
Save-Shot $slug '01-import' -Callouts $import, $row, $workspace

$deploy = Find-Ui -Type Button -Name 'Deploy mods'
Invoke-Ui $deploy
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-Shot $slug '02-deploy' -Callouts $deploy, (Get-ShotStatusElement) -Region (Get-ShotStatusElement), $deploy

Invoke-Ui $import
Complete-FileDialog 'Add a mod' $flashlight
Invoke-Ui $deploy
Wait-ShotStatus 'Built patch.dat*plugin*' | Out-Null
Add-SessionPatch
$row = Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'flashlight*'
Select-Ui $row
Save-Shot $slug '03-plugins' -Callouts $row, (Get-ShotStatusElement)
if (-not (Test-Path 'C:\Games\Far Cry 2\bin\plugins\flashlight\Flashlight.dll')) { throw 'Deploy did not put the plugin in bin\plugins' }

Invoke-Ui (Find-Ui -Type Button -Name 'Revert to original')
$dialog = Wait-Ui { Find-ShotWindow 'Remove all mods' } -What 'the revert dialog'
Save-Shot $slug '04-revert' -Window $dialog -Screen
Push-ShotButton $dialog '1'
# GOG ships a different 1.03 patch.dat than the Steam one JackAll's hashes come from.
$gog = Wait-Ui { if ((Get-ShotStatus) -like 'All mods removed*') { 'none' } else { Find-ShotWindow 'JackAll' } } -What 'revert to finish'
if ($gog -ne 'none') {
    Save-Shot $slug '05-gog-warning' -Window $gog -Screen
    Push-ShotButton $gog 'OK'
}
Wait-ShotStatus 'All mods removed*' | Out-Null
Add-SessionPatch
if (Test-Path 'C:\Games\Far Cry 2\bin\plugins\flashlight\Flashlight.dll') { throw 'Revert left the plugin behind' }

Stop-ShotApp
