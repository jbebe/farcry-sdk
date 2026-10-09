# Renaming a weapon: the HUD name in the archetype, the rest in the string table as a fragment.
$slug = 'renaming'
$inputs = Join-Path (Get-ShotPaths).Bin 'inputs'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900 | Out-Null

function Set-DisplayName([string]$World) {
    Select-UiTab 'Archetypes'
    $picker = Find-Ui -Id WorldPicker
    Wait-Ui { $picker.Current.IsEnabled } -Timeout 60 -What 'the world picker' | Out-Null
    Select-UiCombo $picker $World
    Invoke-Ui (Find-Ui -Id LoadButton)
    Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like '*archetypes*' } -Timeout 300 -What 'the archetypes' | Out-Null
    Set-UiValue (Find-Ui -Id SearchBox) 'AK47'
    $tree = Find-Ui -Id ArchetypeTree
    Expand-Ui (Find-UiByText -Scope $tree -Like 'WeaponProperties*')
    Expand-Ui (Find-UiByText -Scope $tree -Like 'Primary*')
    Select-Ui (Find-UiByText -Scope $tree -Like 'AK47*')
    $cw = Open-UiSection 'CWeaponProperties*' $null
    $cp = Open-UiSection 'CommonProperties*' $cw
    $field = Find-UiField 'sDisplayName' $cp
    Set-UiValue $field 'AK-47 Drum'
    (Find-UiField 'fReloadTime' $cp).SetFocus()
    return $field
}

$field = Set-DisplayName 'world1'
$save = Find-Ui -Type Button -Name 'Save'
Wait-Ui { $save.Current.IsEnabled } -What 'Save to enable' | Out-Null
Show-Ui $field
Save-Shot $slug '01-display-name' -Callouts $field, $save -Region $field, $save, (Find-Ui -Type Text -Name 'sName')
Invoke-Ui $save
Set-DisplayName 'world2' | Out-Null
Invoke-Ui (Find-Ui -Type Button -Name 'Save')
Wait-Ui { Test-Path (Join-Path (Get-ShotPaths).App 'workspace\mods\worlds\world2\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml') } -What 'the world2 save' | Out-Null

# The whole table as XML: JackAll refuses it back.
Select-UiTab 'Files'
Click-Ui (Open-FilesFolder 'languages\english') -X 0.3
Click-Ui (Select-UiRow '*oasisstrings.rml' (Find-Ui -Id FileGrid))
$table = Join-Path $inputs 'oasisstrings.english.xml'
if (Test-Path $table) { Remove-Item $table }
Invoke-Ui (Find-UiButton 'Export XML...')
Complete-FileDialog 'Export decoded XML*' $table
Wait-Ui { Test-Path $table } -Timeout 60 -What 'the exported table' | Out-Null
[IO.File]::WriteAllText($table, [IO.File]::ReadAllText($table).Replace('enum="ak47" value="AK-47"', 'enum="ak47" value="AK-47 Drum"'), (New-Object Text.UTF8Encoding $false))
Invoke-Ui (Find-UiButton 'Import XML...')
Complete-FileDialog 'Import - select*' $table
$refused = Wait-Ui { Find-ShotWindow 'JackAll' } -What 'the refusal'
if ((Get-DialogText $refused) -notmatch 'fragment') { throw 'The whole-table import was not refused' }
Save-Shot $slug '02-refused' -Window $refused -Screen
Push-ShotButton $refused 'OK'

# The fragment, as a text editor would save it, then Rescan.
$fragment = Join-Path (Get-ShotPaths).App 'workspace\mods\languages\english\oasisstrings.fragment.xml'
New-Item -ItemType Directory -Force (Split-Path $fragment) | Out-Null
Copy-Item (Join-Path $PSScriptRoot '..\fixtures\oasisstrings.fragment.xml') $fragment
Select-UiTab 'Mods'
$rescan = Find-UiButton 'Rescan mods'
Invoke-Ui $rescan
Wait-ShotStatus 'Mods rescanned*' | Out-Null
Select-Ui (Find-UiByText -Scope (Find-Ui -Id ModGrid) -Type DataItem -Like 'workspace*')
$files = Find-Ui -Id ModFileTree
for ($i = 0; $i -lt 8; $i++) {
    foreach ($node in @(Find-Ui -Scope $files -Type TreeItem -All -Optional)) {
        $p = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) -and $p.Current.ExpandCollapseState -eq 'Collapsed') { $p.Expand() }
    }
    Start-Sleep -Milliseconds 200
}
Save-Shot $slug '03-rescan' -Callouts $rescan, (Find-UiByText -Scope $files -Like '*oasisstrings*')

Select-UiTab 'Files'
Set-UiToggle (Find-Ui -Type CheckBox -Name 'Show only mod files') $true
Click-Ui (Open-FilesFolder 'languages\english') -X 0.3
Click-Ui (Select-UiRow '*oasisstrings.rml' (Find-Ui -Id FileGrid))
$diff = Wait-Ui { Find-Ui -Type Text -Like 'Showing only the changed lines*' -Optional } -What 'the diff'
Save-Shot $slug '04-diff' -Callouts (Find-Ui -Type Text -Like 'Mod: workspace*'), $diff

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
