# Export, replace, mirror and revert, on the Dart Rifle shop icon VSS Vintorez replaces.
$slug = 'export-replace-revert'
$out = Join-Path (Get-ShotPaths).Bin 'inputs\exports'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Reset-ShotState -Mods 'C:\Projects\FarCry2\mods\vss-vintorez\vss-vintorez-1.1.0.zip'
Start-ShotApp -Width 1440 -Height 900 | Out-Null
Select-UiTab 'Files'
$filter = Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit
$grid = Find-Ui -Id FileGrid
Set-UiValue $filter 'gun_icon_sniperdart'
$row = Select-UiRow 'gun_icon_sniperdart.xbt*' $grid

$origin = Find-Ui -Type Text -Like 'Mod: vss-vintorez*'
Save-Shot $slug '01-buttons' -Callouts $origin, (Find-UiButton 'Export...'), (Find-UiButton 'Export original...'), (Find-UiButton 'Replace...'), (Find-UiButton 'Mirror...'), (Find-UiButton 'Mirror original...')

Invoke-Ui (Find-UiButton 'Export original...')
Complete-FileDialog 'Export original file' (Join-Path $out 'gun_icon_sniperdart.original.xbt')
Wait-ShotStatus 'Exported the base game version*' | Out-Null

Invoke-Ui (Find-UiButton 'Mirror original...')
Wait-ShotStatus '*(original) mirrored into your workspace*' | Out-Null
$row = Select-UiRow 'gun_icon_sniperdart.xbt*' $grid
$revert = Find-UiButton 'Revert'
Save-Shot $slug '02-mirrored' -Callouts (Find-Ui -Type Text -Like 'Mod: workspace*'), $revert, (Get-ShotStatusElement)

Invoke-Ui $revert
Wait-ShotStatus '*is back to the game*' | Out-Null
Select-UiRow 'gun_icon_sniperdart.xbt*' $grid | Out-Null
Invoke-Ui (Find-UiButton 'Replace...')
Complete-FileDialog 'Replace*' (Join-Path $out 'gun_icon_sniperdart.original.xbt')
Wait-ShotStatus '*staged in your workspace*' | Out-Null

# A folder the search didn't already highlight, so the click really selects it.
$guns = Open-FilesFolder 'ui\textures\hud'
Click-Ui $guns -Right
$menu = Wait-Ui { Find-ShotMenu } -What 'the folder menu'
Save-Shot $slug '03-folder-menu' -Screen -NoPark -Callouts (Find-Ui -Scope $menu -Type MenuItem -Like 'Export folder*') -Region $guns, $menu
Invoke-Ui (Find-Ui -Scope $menu -Type MenuItem -Like 'Export folder*')
Complete-FileDialog 'Export *' $out
Close-ShotDialog 'Export folder' 'OK'
if ((Wait-ShotStatus 'Exported*') -notlike '*from ui\textures\hud *') { throw 'Export folder exported the wrong folder' }

Set-UiValue $filter 'ext:xbt sniperdart'
$rows = @(Find-UiByText -Scope $grid -Type DataItem -Like '*_icon_sniperdart.xbt*' -All | Select-Object -First 2)
if ($rows.Count -lt 2) { $rows = @(Wait-Ui { $r = @(Find-UiByText -Scope $grid -Type DataItem -Like '*_icon_sniperdart.xbt*' -All); if ($r.Count -ge 2) { , $r } } -What 'both icons') }
Select-Ui $rows[0]
$rows[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection()
Save-Shot $slug '04-multi' -Callouts $rows[0], $rows[1], (Find-UiButton 'Export all...')

$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\ui\textures\guns\gun_icon_sniperdart.xbt'
if (-not (Test-Path $staged)) { throw 'Replace did not stage the icon' }
Stop-ShotApp
