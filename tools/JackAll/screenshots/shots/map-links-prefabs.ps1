# Links, triggers and prefabs: a safehouse's proximity trigger, starting a link from it, and the
# building prefab it belongs to saved to the library.
$slug = 'map-links-prefabs'
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
Set-UiToggle (Wait-Ui { Get-LayerBox 'Event links' } -What 'the Layers panel') $true
Set-UiToggle (Get-LayerBox 'Vegetation') $false
Click-Ui $layers

$hierarchy = Find-Ui -Id Hierarchy
$tree = Find-Ui -Scope $hierarchy -Id Tree
Set-UiValue @(Find-Ui -Scope $hierarchy -Type Edit -All)[0] 'SafehouseCheck'
$main = @(Find-Ui -Scope $tree -Type TreeItem -All -Children)[0]
for ($round = 0; $round -lt 5; $round++) {
    foreach ($node in @($main) + @(Find-Ui -Scope $main -Type TreeItem -All -Optional)) {
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
    }
    Start-Sleep -Milliseconds 300
}
# Rows start with their eye and lock icons; a group's count is in brackets.
$trigger = @(Find-UiByText -Scope $main -Like '*ProximityTrigger_SafehouseCheck_*' -All) | Where-Object { (Get-UiText $_) -notmatch '\(\d' } | Select-Object -First 1
Click-Ui $trigger -X 0.7
$title = Wait-Ui { @(Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -All) | Where-Object { $_.Current.Name -like 'ProximityTrigger_SafehouseCheck_*' } | Select-Object -First 1 } -What 'the trigger'
Start-Sleep 3
$linkTo = Find-UiButton 'Link to...'
Save-Shot $slug '01-trigger' -Callouts $trigger, $title, $linkTo

Click-Ui $linkTo
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like 'Click the entity*' } -What 'the link prompt' | Out-Null
Save-Shot $slug '02-link-to' -Callouts $linkTo, (Find-Ui -Id StatusText) -Region (Find-Ui -Id StatusText), (Find-Ui -Id Inspector)
Send-ShotKeys 'Esc'

# Two safehouse triggers grouped into a prefab, and the prefab kept in the library.
function Get-Triggers { @(Find-Ui -Scope $main -Type TreeItem -All) | Where-Object { (Get-UiText $_) -notmatch '\(\d' -and (Get-UiText $_) -like '*ProximityTrigger*' } }
Click-Ui (Get-Triggers)[0] -X 0.7
Click-Ui (Get-Triggers)[1] -X 0.7 -Ctrl
Wait-Ui { (@(Find-Ui -Scope (Find-Ui -Id Inspector) -Type Text -All)[0]).Current.Name -like '*(+1 more selected)*' } -What 'two triggers selected' | Out-Null
$group = Find-UiButton 'Group'
Invoke-Ui $group
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like 'Grouped 2 entities*' } -What 'the group' | Out-Null
$savePrefab = Find-UiButton 'Save prefab'
Invoke-Ui $savePrefab
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like 'Saved * to the library*' } -What 'the prefab to be saved' | Out-Null
$library = Find-Ui -Id Library
Expand-Ui (Find-UiByText -Scope $library -Type Group -Like 'Prefabs*')
$saved = Wait-Ui { @(Find-Ui -Scope (Find-Ui -Scope $library -Id PrefabList) -Type ListItem -All -Optional)[0] } -What 'the prefab in the library'
Start-Sleep 2
Save-Shot $slug '03-prefab' -Callouts $group, $savePrefab, (Find-Ui -Id StatusText), $saved
Stop-ShotApp
