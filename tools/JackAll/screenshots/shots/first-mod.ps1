# Your first mod: the AK-47 magazine from 30 to 60 rounds, in both campaign worlds.
$slug = 'first-mod'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900 | Out-Null
Select-UiTab 'Archetypes'

function Open-Ak47([string]$World) {
    $picker = Find-Ui -Id WorldPicker
    Wait-Ui { $picker.Current.IsEnabled } -Timeout 60 -What 'the world picker' | Out-Null
    Select-UiCombo $picker $World
    Invoke-Ui (Find-Ui -Id LoadButton)
    Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like '*archetypes*' } -Timeout 300 -What 'the archetypes' | Out-Null
    Set-UiValue (Find-Ui -Id SearchBox) 'AK47'
    $tree = Find-Ui -Id ArchetypeTree
    Expand-Ui (Find-UiByText -Scope $tree -Like 'WeaponProperties*')
    Expand-Ui (Find-UiByText -Scope $tree -Like 'Primary*')
    $item = Find-UiByText -Scope $tree -Like 'AK47*'
    Select-Ui $item
    return $item
}

function Set-Magazine([int]$Rounds) {
    $cw = Open-UiSection 'CWeaponProperties*' $null
    $cp = Open-UiSection 'CommonProperties*' $cw
    $ammo = Open-UiSection 'Ammo -*' $cp
    $field = Find-UiField 'iAmmoInClip' $ammo
    Set-UiValue $field "$Rounds"
    (Find-UiField 'iMaxAmmoCasual' $ammo).SetFocus()
    return $field
}

$item = Open-Ak47 'world1'
$tab = Find-Ui -Scope (Find-Ui -Id MainTabs) -Type TabItem -Name 'Archetypes' -Children
Save-Shot $slug '01-load' -Callouts $tab, (Find-Ui -Id WorldPicker), (Find-Ui -Id LoadButton), (Find-Ui -Id StatusText) -Region $tab, (Find-Ui -Id StatusText), (Find-Ui -Id SearchBox)
Save-Shot $slug '02-find' -Callouts (Find-Ui -Id SearchBox), $item

$field = Set-Magazine 60
$save = Find-Ui -Type Button -Name 'Save'
Wait-Ui { $save.Current.IsEnabled } -What 'Save to enable' | Out-Null
Save-Shot $slug '03-edit' -Callouts $field, $save -Region (Find-Ui -Id OutlineTree), $save, $field
Invoke-Ui $save
$fragment = Join-Path (Get-ShotPaths).App 'workspace\mods\worlds\world1\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml'
Wait-Ui { Test-Path $fragment } -What 'the world1 fragment' | Out-Null
if ((Get-Content -Raw $fragment) -notmatch 'iAmmoInClip" type="Int32">60<') { throw 'The saved fragment does not hold 60' }

# The list only counts an edit once the library is loaded again.
$item = Open-Ak47 'world1'
Start-Sleep 1
$mods = Find-UiByText -Scope (Find-Ui -Id ModList) -Type ListItem -Like '*workspace*'
Save-Shot $slug '04-saved' -Callouts $item, $mods

Open-Ak47 'world2' | Out-Null
Set-Magazine 60 | Out-Null
Invoke-Ui (Find-Ui -Type Button -Name 'Save')
Wait-Ui { Test-Path ($fragment -replace 'world1', 'world2') } -What 'the world2 fragment' | Out-Null

Select-UiTab 'Mods'
Select-Ui (Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'workspace*')
$files = Find-Ui -Id ModFileTree
for ($i = 0; $i -lt 8; $i++) {
    foreach ($node in @(Find-Ui -Scope $files -Type TreeItem -All -Optional)) {
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
    }
    Start-Sleep -Milliseconds 200
}
Save-Shot $slug '05-workspace' -Callouts (Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'workspace*'), $files

Invoke-Ui (Find-Ui -Type Button -Name 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
