# Which copy does the game read: an edit to world1's DLC quad, which the DLC library overrides.
$slug = 'archetypes'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900

function Set-DustFactor([string]$Section) {
    $cv = Open-UiSection $Section $null
    $field = Find-UiField 'fDustFactor' $cv
    Set-UiValue $field '0.5'
    (Find-UiField 'fWindFactor' $cv).SetFocus()
    return $field
}

function Open-Quad {
    Select-UiTab 'Archetypes'
    $picker = Find-Ui -Id WorldPicker
    Wait-Ui { $picker.Current.IsEnabled } -Timeout 60 -What 'the world picker' | Out-Null
    Select-UiCombo $picker 'world1'
    Invoke-Ui (Find-Ui -Id LoadButton)
    Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like '1,456 archetypes*' } -Timeout 300 -What 'the archetypes' | Out-Null
    Set-UiValue (Find-Ui -Id SearchBox) 'DLC_Vehicle1'
    $tree = Find-Ui -Id ArchetypeTree
    Expand-Ui (Find-UiByText -Scope $tree -Like 'vehicle*')
    Expand-Ui (Find-UiByText -Scope $tree -Like 'Land*')
    $item = Find-UiByText -Scope $tree -Like 'DLC_Vehicle1_DLC1 *'
    Select-Ui $item
    Start-Sleep 1
    return $item
}

# 1. Edit world1's copy from the Files tab, where nothing says it's the wrong one.
Select-UiTab 'Files'
Click-Ui (Open-FilesFolder 'worlds\world1\generated\entitylibrary.fcb\vehicle\Land') -X 0.3
$grid = Find-Ui -Id FileGrid
Click-Ui (Select-UiRow '*\DLC_Vehicle1_DLC1.xml' $grid)
$open = Find-UiButton 'Open value editor...'
Save-Shot $slug '01-files' -Callouts (Select-UiRow '*\DLC_Vehicle1_DLC1.xml' $grid), $open
Invoke-Ui $open
$outline = Find-Ui -Id OutlineTree
Expand-Ui @(Find-Ui -Scope $outline -Type TreeItem -All)[0]
Select-Ui (Find-UiByText -Scope $outline -Like 'Entity*')
$field = Set-DustFactor 'CVehicle - datsun*'
$save = Find-Ui -Type Button -Name 'Save'
Wait-Ui { $save.Current.IsEnabled } -What 'Save to enable' | Out-Null
Save-Shot $slug '02-edit' -Callouts $field, $save
Invoke-Ui $save
$dead = Join-Path (Get-ShotPaths).App 'workspace\mods\worlds\world1\generated\entitylibrary.fcb\vehicle\land\dlc_vehicle1_dlc1.xml'
Wait-Ui { Test-Path $dead } -What 'the world1 fragment' | Out-Null

# 2. The Archetypes tab says it's dead.
$item = Open-Quad
$row = Find-UiByText -Scope (Find-Ui -Id ModList) -Type ListItem -Like 'dead*'
Save-Shot $slug '03-dead' -Callouts $item, $row, (Find-UiByText -Type Group -Like 'CFileDescriptorComponent*')

# 3. So does Check for dead edits.
Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Check for dead edits')
$lint = Wait-Ui { Find-ShotWindow 'JackAll' } -Timeout 120 -What 'the dead edit list'
if ((Get-DialogText $lint) -notmatch 'DLC_Vehicle1_DLC1') { throw 'Check for dead edits missed the quad' }
Save-Shot $slug '04-lint' -Window $lint -Screen
Push-ShotButton $lint 'OK'

# 4. The fix: the same edit on the copy the game reads.
Open-Quad | Out-Null
Set-DustFactor 'CVehicle - quad*' | Out-Null
Invoke-Ui (Find-Ui -Type Button -Name 'Save')
Wait-Ui { Test-Path (Join-Path (Get-ShotPaths).App 'workspace\mods\downloadcontent\dlc1\generated\entitylibrary.fcb\vehicle\land\dlc_vehicle1_dlc1.xml') } -What 'the DLC fragment' | Out-Null
$item = Open-Quad
$deadRow = Find-UiByText -Scope (Find-Ui -Id ModList) -Type ListItem -Like 'dead*'
$live = Find-UiByText -Scope (Find-Ui -Id ModList) -Type ListItem -Like 'the game reads this*'
Save-Shot $slug '05-fixed' -Callouts $deadRow, $live -Region $item, $deadRow, $live

# 5. Drop the dead copy again.
Select-UiTab 'Files'
Click-Ui (Open-FilesFolder 'worlds\world1\generated\entitylibrary.fcb\vehicle\Land') -X 0.3
Click-Ui (Select-UiRow '*\DLC_Vehicle1_DLC1.xml' (Find-Ui -Id FileGrid))
Invoke-Ui (Find-UiButton 'Revert')
Wait-Ui { -not (Test-Path $dead) } -What 'Revert to remove the dead copy' | Out-Null

Save-ShotResult $slug
Stop-ShotApp
