# Tune the enemy AI: one edit in each section of the AI tab, saved together and deployed.
$slug = 'ai'

# Types a value into a grid row's box and moves focus to Away, which is when the row commits it.
# A commit can rebuild the rows, so the row returned is found again.
function Set-RowValue($Grid, [string]$Like, [string]$Value, $Away, [int]$Box = 0) {
    $find = { Wait-Ui { @(Find-Ui -Scope $Grid -Type DataItem -All -Optional) | Where-Object { (Get-UiText $_) -like $Like } | Select-Object -First 1 } -What "the row '$Like'" }
    $row = & $find
    Show-Ui $row
    $edits = Find-Ui -Scope $row -Type Edit -All
    $edits[$Box].SetFocus()
    Set-UiValue $edits[$Box] $Value
    $Away.SetFocus()
    Start-Sleep -Milliseconds 300
    return & $find
}

function Select-Section([string]$Name) {
    Select-Ui (Find-Ui -Scope (Find-Ui -Id AiSections) -Type TabItem -Name $Name -Children)
    Start-Sleep -Milliseconds 400
}

# Soldiers and Weapons are the same view; this one waits for the shown section's list to fill.
function Wait-Archetypes {
    Wait-Ui { $l = Find-Ui -Id AiArchetypeList -Optional; if ($l -and @(Find-Ui -Scope $l -Type CheckBox -All -Optional).Count -gt 0) { $l } } -Timeout 300 -What 'the archetype list'
}

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000 | Out-Null
Select-UiTab 'AI'

# One archetype only: ticking several saves just the first of them (see the page's warning).
$list = Wait-Archetypes
$filter = Find-Ui -Id AiArchetypeFilter
Set-UiValue $filter 'Sniper'
$sniper = Wait-Ui { @(Find-Ui -Scope $list -Type CheckBox -All -Optional) | Where-Object { (Get-UiText $_) -eq 'Blue_Faction.Sniper_Caucasian' } | Select-Object -First 1 } -What 'the sniper'
Set-UiToggle $sniper $true
$summary = Wait-Ui { Find-Ui -Type Text -Name 'Editing Blue_Faction.Sniper_Caucasian.' -Optional } -What 'the sniper to be ticked'
$reaction = Set-RowValue (Find-Ui -Id AiTuningFields) 'Reaction time (s)*' '1' $filter
Save-Shot $slug '01-soldiers' -Callouts $filter, $sniper, $summary, $reaction

Select-Section 'Weapons'
$list = Wait-Archetypes
$filter = Find-Ui -Id AiArchetypeFilter
Set-UiValue $filter 'AK47'
$ak = Wait-Ui { @(Find-Ui -Scope $list -Type CheckBox -All -Optional) | Where-Object { (Get-UiText $_) -eq 'Primary.AK47' } | Select-Object -First 1 } -What 'the AK-47'
Set-UiToggle $ak $true
$fields = Find-Ui -Id AiTuningFields
$least = Set-RowValue $fields 'Infamous: at least*' '1' $filter
Set-RowValue $fields 'Infamous: at most*' '2' $filter | Out-Null
Save-Shot $slug '02-weapons' -Callouts $ak, $least

Select-Section 'Curves'
$curves = Wait-Ui { $l = Find-Ui -Id AiCurveList -Optional; if ($l -and @(Find-Ui -Scope $l -Type ListItem -All -Optional).Count -gt 0) { $l } } -Timeout 300 -What 'the curves'
$curve = Find-UiByText -Scope $curves -Type ListItem -Like 'ShootingSystem.DistanceAccuracy'
Select-Ui $curve
$last = Set-RowValue (Find-Ui -Id AiCurvePoints) '17 *' '0' (Find-Ui -Id AiCurveFilter) 1
Save-Shot $slug '03-curves' -Callouts $curve, $last

Select-Section 'Behaviour odds'
$grid = Wait-Ui { $g = Find-Ui -Id AiBehaviorGrid -Optional; if ($g -and @(Find-Ui -Scope $g -Type DataItem -All -Optional).Count -gt 0) { $g } } -Timeout 60 -What 'the behaviours'
$which = Find-Ui -Id FillBehavior
Expand-Ui $which
Select-Ui (Find-UiByText -Scope $which -Type ListItem -Like 'ShootInterestingObject')
$which.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
Set-UiValue (Find-Ui -Id FillFrom) '0'
Set-UiValue (Find-Ui -Id FillValue) '0'
$fill = Find-UiButton 'Fill'
Invoke-Ui $fill
$row = Wait-Ui { @(Find-Ui -Scope $grid -Type DataItem -All) | Where-Object { (Get-UiText $_) -match '^ShootInterestingObject( 0){28}$' } | Select-Object -First 1 } -What 'the filled row'
Save-Shot $slug '04-behaviours' -Callouts $row, $which, $fill

Select-Section 'Brains'
Wait-Ui { $t = Find-Ui -Id AiBrainTree -Optional; if ($t -and @(Find-Ui -Scope $t -Type TreeItem -All -Optional).Count -gt 0) { $t } } -Timeout 300 -What 'mercbrain' | Out-Null
$search = Find-Ui -Id AiBrainSearch
Set-UiValue $search 'Wait2Sec'
$hit = Find-UiByText -Scope (Find-Ui -Id AiBrainSearchResults) -Type ListItem -Like 'Wait2Sec ::*GunHandling/Wait2Sec'
Select-Ui $hit
$wait = Set-RowValue (Find-Ui -Id AiBrainParameters) 'timeToWait *' '5' $search
$links = Find-Ui -Id AiBrainLinks
Find-UiByText -Scope $links -Type ListItem -Like '*HolsterWeapon*' | Out-Null
Save-Shot $slug '05-brains' -Callouts $search, $hit, $wait, $links

$save = Find-Ui -Id AiSaveButton
Wait-Ui { $save.Current.IsEnabled } -What 'Save to enable' | Out-Null
Invoke-Ui $save
$staged = Wait-Ui { Find-Ui -Type Text -Like 'Staged *' -Optional } -Timeout 180 -What 'the staged message'
if ($staged.Current.Name -ne 'Staged 1 archetype(s), 1 archetype(s), 1 curve(s), behaviour odds, mercbrain.ai.rml into the workspace') {
    throw "Unexpected save: $($staged.Current.Name)"
}
$mods = Join-Path (Get-ShotPaths).App 'workspace\mods'
foreach ($file in 'engine\gamemodes\gamemodesconfig.xml', 'scripts\game\newbrains\mercbrain.ai.rml',
                  'worlds\world1\generated\entitylibrary.fcb\enemy_archetypes\blue_faction\sniper_caucasian.xml',
                  'worlds\world2\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml',
                  'worlds\world2\generated\entitylibrary.fcb\curves\shootingsystem\distanceaccuracy.xml') {
    if (-not (Test-Path (Join-Path $mods $file))) { throw "$file was not staged" }
}

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
