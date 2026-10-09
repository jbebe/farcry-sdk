# Change which animation plays: the AK-47's first-person reload rule, played 25% faster.
$slug = 'animations'

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000
Select-UiTab 'Animations'
$weapon = Wait-Ui { $x = Find-Ui -Id MoveWeaponPicker -Optional; if ($x -and $x.Current.IsEnabled) { $x } } -Timeout 120 -What 'the weapon list'
Select-UiCombo $weapon 'AK47'
$filter = Find-Ui -Id MoveStateFilter
Set-UiValue $filter 'Reload'
$state = Find-UiByText -Scope (Find-Ui -Id MoveStateList) -Type ListItem -Like 'Pawn_Generic_Reload'
Select-Ui $state
$grid = Find-Ui -Id MoveRulesGrid
$rule = Wait-Ui { @(Find-Ui -Scope $grid -Type DataItem -All -Optional) | Where-Object { (Get-UiText $_) -like '*1stge_uppb_reload*' } | Select-Object -First 1 } -What 'the first-person reload rule'
Save-Shot $slug '01-situations' -Callouts (Find-Ui -Id MoveGraphPicker), $weapon, $filter, $state, $grid

Select-Ui $rule
$clip = Wait-Ui { Find-Ui -Id MoveClipPath -Optional } -What 'the rule details'
# Speed sits in a row of labelled boxes; the box is the one right under its label.
$label = Find-Ui -Type Text -Like 'Speed*'
$l = $label.Current.BoundingRectangle
$speed = @(Find-Ui -Type Edit -All) | Where-Object { $r = $_.Current.BoundingRectangle; [Math]::Abs($r.X - $l.X) -lt 8 -and $r.Y -gt $l.Y -and $r.Y -lt $l.Y + 40 } | Select-Object -First 1
Set-UiValue $speed '1.25'
$apply = Find-Ui -Id MoveApplyClip
Save-Shot $slug '02-rule' -Callouts $rule, $clip, $speed, $apply, (Find-Ui -Id MoveConditionList)

Invoke-Ui $apply
$save = Find-Ui -Id MoveSaveButton
Wait-Ui { $save.Current.IsEnabled } -What 'Save to enable' | Out-Null
Invoke-Ui $save
$move = Join-Path (Get-ShotPaths).App 'workspace\mods\graphics\move\movemgr.bin'
Wait-Ui { (Test-Path $move) -and @(Get-ChildItem $move -File).Count -gt 0 } -Timeout 60 -What 'the state to be staged' | Out-Null
Start-Sleep 1
$staged = Wait-Ui { Find-Ui -Type Text -Like 'Staged *' -Optional } -What 'the staged message'
Save-Shot $slug '03-saved' -Callouts $save, $staged -Region (Find-Ui -Id MoveGraphPicker), $staged

Invoke-ShotDeploy
Save-ShotResult $slug
Stop-ShotApp
