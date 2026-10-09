# Explore a world: the Map tab's layout, its layers, the navmesh, and finding one object.
$slug = 'map-viewer'
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
$load = Find-Ui -Id LoadButton
Invoke-Ui $load
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -match '^world1: \d+x\d+ sectors' } -Timeout 900 -What 'world1 to load' | Out-Null
Start-Sleep 3
$viewport = Find-Ui -Id Viewport -Optional
Save-Shot $slug '01-overview' -Callouts $picker, $load, (Find-Ui -Id Hierarchy), $viewport, (Find-Ui -Id Inspector), (Find-Ui -Id Library)

# The Layers panel: its checkboxes carry their label as a text inside them.
function Get-LayerBox([string]$Label) { $walker.GetParent((Find-Ui -Type Text -Name $Label)) }
$layers = Find-Ui -Id LayersButton
Click-Ui $layers
$navmesh = Wait-Ui { Get-LayerBox 'Navmesh' } -What 'the Layers panel'
Set-UiToggle $navmesh $true
Start-Sleep 2
Save-Shot $slug '02-layers' -Screen -NoPark -Callouts $navmesh, (Find-Ui -Id NavMeshSurface) -Region $layers, (Find-UiButton 'Uncheck all'), (Find-Ui -Type Text -Name 'Surface types')
Click-Ui $layers
Start-Sleep 3
Save-Shot $slug '03-navmesh' -Photo -Region $viewport

# One object: filter the hierarchy, open its groups, click it.
$hierarchy = Find-Ui -Id Hierarchy
Set-UiValue @(Find-Ui -Scope $hierarchy -Type Edit -All)[0] 'jeep'
$tree = Find-Ui -Scope $hierarchy -Id Tree
for ($round = 0; $round -lt 6; $round++) {
    foreach ($node in @(Find-Ui -Scope $tree -Type TreeItem -All -Optional)) {
        if ((Get-UiText $node) -match 'missions|benchmark') { continue }
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
    }
    Start-Sleep -Milliseconds 300
}
Click-Ui $layers
Set-UiToggle (Get-LayerBox 'Navmesh') $false
Set-UiToggle (Get-LayerBox 'Vegetation') $false
Click-Ui $layers
# A row's text starts with its eye and lock icons; the group above it also ends with the jeep's name.
function Get-JeepRow { @(Find-UiByText -Scope $tree -Like '*Land.JeepLiberty_0' -All) | Where-Object { (Get-UiText $_) -notlike '*(1)*' } | Select-Object -First 1 }
Click-Ui (Get-JeepRow) -X 0.7
$title = Wait-Ui { Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -Name 'Land.JeepLiberty_0' -Optional } -What 'the jeep to be selected'
Start-Sleep 3
Save-Shot $slug '04-selected' -Callouts (Get-JeepRow), $title, (Find-UiButton 'Open archetype'), (Find-Ui -Type Text -Name 'TRANSFORM')

Stop-ShotApp
