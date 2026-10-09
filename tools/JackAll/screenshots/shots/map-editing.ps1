# Move, add and delete objects: the Liberty jeep moved, a barrel stack added, a Wrangler deleted, Check
# catching a bad move, and Save.
$slug = 'map-editing'
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000 | Out-Null
Select-UiTab 'Map'
$picker = Find-Ui -Id MapPicker
Wait-Ui { $picker.Current.IsEnabled } -Timeout 60 -What 'the map list' | Out-Null
Expand-Ui $picker
Select-Ui @(Find-Ui -Scope $picker -Type ListItem -All)[0]
$p = $null
if ($picker.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Collapse() }
Invoke-Ui (Find-Ui -Id LoadButton)
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -match '^world1: \d+x\d+ sectors' } -Timeout 900 -What 'world1 to load' | Out-Null

function Get-LayerBox([string]$Label) { $walker.GetParent((Find-Ui -Type Text -Name $Label)) }
$layers = Find-Ui -Id LayersButton
Click-Ui $layers
Set-UiToggle (Wait-Ui { Get-LayerBox 'Vegetation' } -What 'the Layers panel') $false
Click-Ui $layers

$hierarchy = Find-Ui -Id Hierarchy
$tree = Find-Ui -Scope $hierarchy -Id Tree
Set-UiValue @(Find-Ui -Scope $hierarchy -Type Edit -All)[0] 'jeep'
# Opens the main layer's groups down to the matching objects; an edit rebuilds the tree collapsed.
function Expand-Matches {
    for ($round = 0; $round -lt 6; $round++) {
        foreach ($node in @(Find-Ui -Scope $tree -Type TreeItem -All -Optional)) {
            if ((Get-UiText $node) -match 'missions|benchmark') { continue }
            $p = $null
            if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
        }
        Start-Sleep -Milliseconds 300
    }
}
Expand-Matches
# A row's text starts with its eye and lock icons, and its group's text ends with the row's.
function Get-Row([string]$Name) { @(Find-UiByText -Scope $tree -Like "*$Name" -All) | Where-Object { (Get-UiText $_) -notmatch '\(\d+\)' } | Select-Object -First 1 }
function Get-Position { @(Find-Ui -Scope (Find-Ui -Id Inspector) -Type Edit -All)[0] }
function Set-Position([string]$Value) {
    Set-UiValue (Get-Position) $Value
    @(Find-Ui -Scope (Find-Ui -Id Inspector) -Type Edit -All)[1].SetFocus()
    Start-Sleep -Milliseconds 500
}

# 1. Move the jeep 5 m east.
Click-Ui (Get-Row 'Land.JeepLiberty_0') -X 0.7
Wait-Ui { Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -Name 'Land.JeepLiberty_0' -Optional } -What 'the jeep' | Out-Null
$start = (Get-UiValue (Get-Position)).Split(',') | ForEach-Object { [double]$_.Trim() }
Set-Position (('{0}, {1}, {2}' -f ($start[0] + 5), $start[1], $start[2]))
Start-Sleep 2
Save-Shot $slug '01-move' -Callouts (Get-Position), (Find-Ui -Id UndoButton), (Find-Ui -Id SaveButton)

# 2. Check: the same jeep moved 300 m away leaves its sector.
Set-Position (('{0}, {1}, {2}' -f ($start[0] + 300), $start[1], $start[2]))
Click-Ui (Find-Ui -Id CheckButton)
$findings = Wait-Ui { Find-Ui -Id FindingsList -Optional } -What 'the Check results'
Start-Sleep 1
Save-Shot $slug '02-check' -Screen -NoPark -Callouts $findings -Region (Find-Ui -Id CheckButton), (Find-Ui -Id CheckSummary), $findings
Send-ShotKeys 'Esc'
Invoke-Ui (Find-Ui -Id UndoButton)
Start-Sleep 1
if ((Get-UiValue (Get-Position)) -notlike ('{0},*' -f ($start[0] + 5))) { throw 'Undo did not bring the jeep back' }

# 3. Add a barrel stack from the layer's menu.
# The layer's own label: the expanded row also spans everything under it.
$main = Find-Ui -Scope @(Find-Ui -Scope $tree -Type TreeItem -All -Children)[0] -Type Text -Like 'main (*'
Click-Ui $main -Right
$menu = Wait-Ui { Find-ShotMenu } -What 'the layer menu'
$new = Find-Ui -Scope $menu -Type MenuItem -Name 'New'
Expand-Ui $new
$fromArchetype = Wait-Ui { @(Find-Ui -Scope $new -Type MenuItem -All -Optional) | Where-Object { $_.Current.Name -like 'Archetype*' } | Select-Object -First 1 } -What 'New > Archetype'
Save-Shot $slug '03-new' -Screen -NoPark -Callouts $fromArchetype -Region $main, $new, $fromArchetype
Invoke-Ui $fromArchetype
$search = Wait-Ui { Find-Ui -Id ArchetypePickerSearch -Optional } -What 'the archetype picker'
Set-UiValue $search 'BarrelBeerStack'
$item = Wait-Ui { @(Find-Ui -Scope (Find-Ui -Id ArchetypePickerList) -Type ListItem -All -Optional)[0] } -What 'a barrel stack'
Select-Ui $item
$window = Find-ShotWindow 'New entity from archetype'
Save-Shot $slug '04-picker' -Window $window -Callouts $search, $item, (Find-Ui -Id ArchetypePickerOk)
Invoke-Ui (Find-Ui -Id ArchetypePickerOk)
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like 'Placed *' } -What 'the barrel to be placed' | Out-Null
Start-Sleep 2
Save-Shot $slug '05-placed' -Callouts (Find-Ui -Id StatusText), (@(Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -All)[0])

# 4. Delete one Wrangler, then save everything.
Expand-Matches
Click-Ui (Get-Row 'Land.JeepWrangler_1') -X 0.7
Wait-Ui { Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -Name 'Land.JeepWrangler_1' -Optional } -What 'the Wrangler' | Out-Null
Invoke-Ui (Find-UiButton 'Delete' -Scope (Find-Ui -Id Inspector))
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like 'Deleted *' } -What 'the Wrangler to be deleted' | Out-Null
Invoke-Ui (Find-Ui -Id SaveButton)
$saved = Wait-Ui { Find-ShotWindow 'Map edits saved' } -Timeout 120 -What 'the save summary'
$text = Get-DialogText $saved
foreach ($expected in 'JeepLiberty_0', 'BarrelBeerStack', '_layout') { if ($text -notmatch $expected) { throw "Save did not stage $expected" } }
Save-Shot $slug '06-saved' -Window $saved -Screen
Push-ShotButton $saved 'OK'

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
