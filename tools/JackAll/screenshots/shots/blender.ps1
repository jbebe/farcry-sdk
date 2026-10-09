# Edit a model in Blender: the AK-47 out as a model pack, its magazine lengthened by the add-on
# (headless, standing in for the hand edit), and the pack applied back.
$slug = 'blender'
$inputs = (Get-ShotPaths).Inputs
$pack = Join-Path $inputs 'ak47.fc2model'
$edited = Join-Path $inputs 'ak47-longer-mag.fc2model'
foreach ($f in $pack, $edited) { if (Test-Path $f) { Remove-Item $f } }
$blender = 'C:\Programs\Blender 5.2\blender.exe'

# The Blender side: the add-on's own screenshots, without the machine's user path on the install one.
$assets = Join-Path $PSScriptRoot '..\..\..\BlenderFC2\assets'
$out = Join-Path (Get-ShotPaths).Images $slug
New-Item -ItemType Directory -Force $out | Out-Null
& (Get-ShotPaths).Ffmpeg -y -v error -i (Join-Path $assets 'extension-install.png') -vf 'crop=iw:430:0:0' (Join-Path $out '02-install.png')
Copy-Item (Join-Path $assets 'import-model.png') (Join-Path $out '03-import.png')
Copy-Item (Join-Path $assets 'outliner.png') (Join-Path $out '04-outliner.png')
Copy-Item (Join-Path $assets 'export-model.png') (Join-Path $out '05-export.png')

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'hash:585ACE93'
$grid = Find-Ui -Id FileGrid
Click-Ui (Select-UiRow '*\ak47.xbg' $grid)
$export = Find-UiButton 'Export as .fc2model...'
Save-Shot $slug '01-export' -Callouts $export, (Find-UiButton '...with animations') -Region (Get-DetailsFrame 'ak47.xbg')

Invoke-Ui $export
Complete-FileDialog 'Export as .fc2model' $pack
Wait-ShotStatus 'Packed *' -Timeout 120 | Out-Null

& $blender --factory-startup -b --python (Join-Path $PSScriptRoot '..\fixtures\blender_longer_mag.py') -- $pack $edited | Out-Null
if (-not (Test-Path $edited)) { throw 'Blender did not write the edited pack' }

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Apply .fc2model')
Complete-FileDialog 'Apply .fc2model' $edited
$confirm = Wait-Ui { Find-ShotWindow 'Apply .fc2model' | Where-Object { (Get-DialogText $_) -match 'Applying this pack' } } -What 'the apply confirmation'
Save-Shot $slug '06-apply' -Window $confirm -Screen
Push-ShotButton $confirm 'Yes'
$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\graphics\weapons\primary\ak47\ak47.xbg'
Wait-Ui { Test-Path $staged } -Timeout 120 -What 'Apply to stage ak47.xbg' | Out-Null

Select-UiTab 'Files'
Click-Ui (Select-UiRow '*\ak47.xbg' $grid)
$frame = Get-DetailsFrame 'ak47.xbg'
Start-Sleep 2
Save-Shot $slug '07-result' -Callouts (Find-Ui -Type Text -Like 'Mod: workspace*') -Region $frame

Invoke-ShotDeploy
Save-ShotResult $slug
Stop-ShotApp
