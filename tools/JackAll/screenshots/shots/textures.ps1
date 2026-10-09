# Replace a texture: the Dart Rifle's shop icon out as DDS + XML, and VSS Vintorez's icon back in as
# the "edited" picture, so the result can be compared with a file that was played.
$slug = 'textures'
$vssIcon = 'C:\Projects\FarCry2\mods\vss-vintorez\layer\mods\ui\textures\guns\gun_icon_sniperdart.xbt'
$edited = Join-Path (Get-ShotPaths).Bin 'inputs\vss-icon'
$exported = Join-Path (Get-ShotPaths).Bin 'inputs\exported-icon'
foreach ($dir in $edited, $exported) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }; New-Item -ItemType Directory -Force $dir | Out-Null }
& (Get-ShotPaths).Cli xbt extract $vssIcon -o $edited | Out-Null

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900 | Out-Null
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'gun_icon_sniperdart'
$grid = Find-Ui -Id FileGrid
Click-Ui (Select-UiRow '*\gun_icon_sniperdart.xbt' $grid)
$export = Find-UiButton 'Export DDS + XML...'
$import = Find-UiButton 'Import DDS + XML...'
Save-Shot $slug '01-preview' -Callouts $export, $import, (Find-Ui -Type Image -Optional)

Invoke-Ui $export
Complete-FileDialog 'Export DDS' (Join-Path $exported 'gun_icon_sniperdart.dds')
Wait-Ui { (Test-Path (Join-Path $exported 'gun_icon_sniperdart.dds')) -and (Test-Path (Join-Path $exported 'gun_icon_sniperdart.xml')) } -What 'the exported pair' | Out-Null

Invoke-Ui $import
Complete-FileDialog 'Import - select*' (Join-Path $edited 'gun_icon_sniperdart.dds'), (Join-Path $edited 'gun_icon_sniperdart.xml')
$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\ui\textures\guns\gun_icon_sniperdart.xbt'
Wait-Ui { Test-Path $staged } -What 'the icon to be staged' | Out-Null
if ((Get-FileHash $staged).Hash -ne (Get-FileHash $vssIcon).Hash) { throw 'The staged icon differs from VSS Vintorez' }
Click-Ui (Select-UiRow '*\gun_icon_sniperdart.xbt' $grid)
Save-Shot $slug '02-imported' -Callouts (Find-Ui -Type Text -Like 'Mod: workspace*'), (Find-Ui -Type Image -Optional), (Find-UiButton 'Revert')

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
