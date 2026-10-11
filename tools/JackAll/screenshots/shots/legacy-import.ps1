# Importing a legacy mod: Skip Intro's full patch.dat, turned into the workspace edits it amounts to.
# Built on Steam's patch, it comes in as only its own files on a GOG install too.
$slug = 'legacy-import'
$zip = Join-Path (Get-ShotPaths).Repo 'tmp\mods\FC2 Skip Intro-320-1-1-1651917084.zip'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900

$import = Find-UiButton 'Import legacy mod'
Invoke-Ui $import
Complete-FileDialog 'Import a legacy mod*' $zip
Wait-Ui { (Get-ShotStatus) -like 'Imported *' } -Timeout 300 -What 'the import' | Out-Null
Start-Sleep 1
if (Find-ShotWindow 'JackAll') { throw "The import raised a notice: $(Get-DialogText (Find-ShotWindow 'JackAll'))" }

# Skip Intro's own changes: the two Domino scripts and the mouse filter in five input maps.
$grid = Find-Ui -Id ModGrid
$workspace = Find-UiByText -Scope $grid -Type DataItem -Like 'workspace*'
Select-Ui $workspace
$files = Find-Ui -Id ModFileTree
Expand-UiTree $files
$count = Wait-Ui { Find-Ui -Type Text -Like '7 files' -Optional } -What "the mod's own seven files"
Save-Shot $slug '01-imported' -Callouts $import, $count, $files, (Get-ShotStatusElement)

Select-UiTab 'Files'
Set-UiToggle (Find-Ui -Type CheckBox -Name 'Show only mod files') $true
$filter = Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit
Set-UiValue $filter 'master_world1'
$row = Select-UiRow '*\master_world1.world1.lua' (Find-Ui -Id FileGrid)
Click-Ui $row
Start-Sleep 1
$frame = Get-DetailsFrame 'master_world1.world1.lua'
Save-Shot $slug '04-diff' -Callouts (Find-Ui -Type CheckBox -Name 'Show only mod files'), $row -Region $frame

Invoke-ShotDeploy
Save-ShotResult $slug
Stop-ShotApp
