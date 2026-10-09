# Importing a legacy mod: Skip Intro's full patch.dat, turned into the workspace edits it amounts to,
# then trimmed to the mod's own files. On a GOG install the import also carries every difference
# between the Steam patch the mod was built on and GOG's; the trim removes those.
$slug = 'legacy-import'
$zip = Join-Path (Get-ShotPaths).Repo 'tmp\mods\FC2 Skip Intro-320-1-1-1651917084.zip'

function Show-TopFolders($Tree) {
    foreach ($node in @(Find-Ui -Scope $Tree -Type TreeItem -Children -All -Optional)) {
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -ne 'Collapsed') { $p.Collapse() }
    }
}

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900

$import = Find-UiButton 'Import legacy mod'
Invoke-Ui $import
Complete-FileDialog 'Import a legacy mod*' $zip
Wait-Ui { (Get-ShotStatus) -like 'Imported *' } -Timeout 300 -What 'the import' | Out-Null
$whole = Wait-Ui { Find-ShotWindow 'JackAll' } -Timeout 30 -What 'the whole-file notice'
if ((Get-DialogText $whole) -notmatch 'entitylibrarypatchoverride\.fcb') { throw "Unexpected notice: $(Get-DialogText $whole)" }
Save-Shot $slug '02-whole-file' -Window $whole -Screen
Push-ShotButton $whole 'OK'
Start-Sleep 1
if (Find-ShotWindow 'JackAll') { throw "A second notice opened: $(Get-DialogText (Find-ShotWindow 'JackAll'))" }

$grid = Find-Ui -Id ModGrid
$workspace = Find-UiByText -Scope $grid -Type DataItem -Like 'workspace*'
Select-Ui $workspace
$files = Find-Ui -Id ModFileTree
Start-Sleep 1
Show-TopFolders $files
$count = Find-Ui -Type Text -Like '1,782 files'
Save-Shot $slug '01-imported' -Callouts $import, $count, $files, (Get-ShotStatusElement)

# Skip Intro's own changes are the two Domino scripts and the mouse filter in the input maps.
$mods = Join-Path (Get-ShotPaths).App 'workspace\mods'
Get-ChildItem $mods -Directory | Where-Object { $_.Name -notin 'config', 'domino' } | Remove-Item -Recurse -Force
$rescan = Find-UiButton 'Rescan mods'
Invoke-Ui $rescan
Wait-ShotStatus 'Mods rescanned*' | Out-Null
$workspace = Find-UiByText -Scope $grid -Type DataItem -Like 'workspace*'
Select-Ui $workspace
$files = Find-Ui -Id ModFileTree
Expand-UiTree $files
Wait-Ui { Find-Ui -Type Text -Like '7 files' -Optional } -What 'the trimmed workspace' | Out-Null
Save-Shot $slug '03-trimmed' -Callouts (Find-Ui -Type Button -Name 'Open location'), $rescan, $files

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
