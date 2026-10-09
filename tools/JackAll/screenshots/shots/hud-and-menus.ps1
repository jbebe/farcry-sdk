# Edit the HUD and menus: the critical-health hint's text recoloured in hud.mgb, saved as fragments.
$slug = 'hud-and-menus'

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000 | Out-Null
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'ext:mgb hud'
$row = Wait-Ui { $r = Get-UiItem (Find-Ui -Id FileGrid) 'ui\localized\pc\eng\ui\hud.mgb'; Select-Ui $r; $r } -Timeout 30 -What 'hud.mgb'
Click-Ui $row
Invoke-Ui (Find-UiButton 'Open in MGB Editor...')
$tree = Wait-Ui { Find-Ui -Id Tree -Optional } -Timeout 60 -What 'the MGB editor'

$package = @(Find-Ui -Scope $tree -Type TreeItem -All -Children)[0]
Expand-Ui $package
Expand-Ui (Find-UiByText -Scope $package -Like 'Areas*')
$page = Find-UiByText -Scope $tree -Like 'Page  #00C1EB17*'
Expand-Ui $page
$text = Find-UiByText -Scope $page -Like 'Text*TUTORIAL_CRITICAL_TEXT_1*'
Select-Ui $text
Show-Ui $text
Save-Shot $slug '01-editor' -Callouts $text, (Find-Ui -Id Properties), (Find-UiButton 'Add...'), (Find-UiButton 'Save changes')

# Every keyframe of the text gets the new colour; each keeps its own alpha, which is the fade.
Expand-Ui $text
$keyframes = Find-UiByText -Scope $text -Like 'Keyframes*'
Expand-Ui $keyframes
$count = @(Find-Ui -Scope $keyframes -Type TreeItem -All -Children).Count
for ($i = 0; $i -lt $count; $i++) {
    $kf = @(Find-Ui -Scope $keyframes -Type TreeItem -All -Children)[$i]
    Expand-Ui $kf
    Select-Ui @(Find-Ui -Scope $kf -Type TreeItem -All -Children)[0]
    Start-Sleep -Milliseconds 400
    $color = Find-UiField 'StateColor' (Find-Ui -Id Properties)
    $old = Get-UiValue $color
    Set-UiValue $color ('0x' + $old.Substring(2, 2) + 'FFA040')
    (Find-UiField 'Height' (Find-Ui -Id Properties)).SetFocus()
    Start-Sleep -Milliseconds 300
    if ($i -eq 1) {
        $row = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($color)
        Save-Shot $slug '02-colour' -Callouts $color, (Find-Ui -Scope $row -Id SwatchButton) -Region (Find-UiField 'Left' (Find-Ui -Id Properties)), $color -Pad 60
    }
}

# Add offers what the selected spot can hold: an area takes elements.
Select-Ui (Find-UiByText -Scope $tree -Like 'Page  #00C1EB17*')
Click-Ui (Find-UiButton 'Add...')
$menu = Wait-Ui { Find-ShotMenu } -What 'the Add list'
Save-Shot $slug '03-add' -Screen -NoPark -Region $menu, (Find-UiButton 'Add...')
Send-ShotKeys 'Esc'

Invoke-Ui (Find-UiButton 'Save changes')
$fragments = Join-Path (Get-ShotPaths).App 'workspace\mods\ui\localized\pc\eng\ui\hud.mgb'
Wait-Ui { (Test-Path $fragments) -and @(Get-ChildItem $fragments -File).Count -gt 0 } -Timeout 60 -What 'the fragments to be staged' | Out-Null
Start-Sleep 1
Save-Shot $slug '04-saved' -Callouts (Find-UiButton 'Save changes'), (Find-Ui -Id StatusText) -Region (Find-Ui -Id HeaderText), (Find-Ui -Id StatusText)

Select-UiTab 'Mods'
Select-Ui (Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'workspace*')
$files = Find-Ui -Id ModFileTree
for ($i = 0; $i -lt 10; $i++) {
    foreach ($node in @(Find-Ui -Scope $files -Type TreeItem -All -Optional)) {
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
    }
    Start-Sleep -Milliseconds 200
}
$fragment = Find-UiByText -Scope $files -Like '*.xml*'
Save-Shot $slug '05-fragments' -Callouts $fragment -Region @(Find-Ui -Scope $files -Type TreeItem -All)[0], $fragment -Pad 30

Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
