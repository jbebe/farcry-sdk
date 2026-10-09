# Finding files: the merged tree, the filter words, mod files, unused files.
$slug = 'finding-files'

Reset-ShotState -Mods (Join-Path (Get-ShotPaths).Repo 'mods\vss-vintorez\vss-vintorez-1.1.0.zip')
Start-ShotApp -Width 1440 -Height 900
Select-UiTab 'Files'
$filter = Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit
$grid = Find-Ui -Id FileGrid

function Get-Row([string]$Like) { Find-UiByText -Scope $grid -Type DataItem -Like $Like }

Set-UiValue $filter 'ext:xbt ak47'
$row = Get-Row 'ak47_state01_m.xbt*'
Save-Shot $slug '01-filter' -Callouts $filter, $row, (Find-Ui -Scope $grid -Type HeaderItem -Name 'Source')

Select-Ui $row
Wait-Ui { (Find-Ui -Type Button -Name 'Copy' -All -Optional).Count -ge 2 } -What 'the details pane' | Out-Null
$copies = @(Find-Ui -Type Button -Name 'Copy' -All)
Save-Shot $slug '02-details' -Callouts $copies[0], $copies[1], (Find-Ui -Type Image -Optional)

Set-UiValue $filter 'hash:585ACE93'
Save-Shot $slug '03-hash' -Callouts $filter, (Get-Row 'ak47.xbg*') -Region $filter, $grid

$onlyMods = Find-Ui -Type CheckBox -Name 'Show only mod files'
Set-UiToggle $onlyMods $true
Set-UiValue $filter 'sniperdart'
$modded = Get-Row 'gun_icon_sniperdart.xbt*'
Save-Shot $slug '04-only-mods' -Callouts $onlyMods, $modded, (Find-Ui -Id FolderTree)
Set-UiToggle $onlyMods $false

Set-UiValue $filter 'presets ext:xml'
$unused = Get-Row 'cryers.xml*'
Select-Ui $unused
$hide = Find-Ui -Type CheckBox -Name 'Hide unused game files'
$warning = Find-Ui -Type Text -Like 'The game never reads this file*'
Save-Shot $slug '05-unused' -Callouts $unused, $warning, $hide

Stop-ShotApp
