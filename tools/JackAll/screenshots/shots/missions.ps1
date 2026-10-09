# Read a mission: the first act's town escape in the Domino graph viewer.
$slug = 'missions'

Reset-ShotState
Start-ShotApp -Width 1600 -Height 1000
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'ext:lua a1sm01_mission'
Click-Ui (Select-UiRow '*\a1sm01_townescape.a1sm01_mission.lua' (Find-Ui -Id FileGrid))
Invoke-Ui (Wait-Ui { Find-Ui -Id OpenButton -Optional } -What 'the Domino launcher')
$fit = Wait-Ui { Find-Ui -Id FitButton -Optional } -Timeout 120 -What 'the graph'
Invoke-Ui $fit
Start-Sleep 2
$find = Find-Ui -Id FindBox
Save-Shot $slug '01-graph' -Callouts $fit, (Find-Ui -Id FocusSelector), $find, (Find-Ui -Id SideTabs)

Set-UiValue $find 'Objective'
Click-Ui $find
Send-ShotKeys 'Enter'
$title = Wait-Ui { $t = Find-Ui -Id NodeTitle -Optional; if ($t -and $t.Current.Name) { $t } } -What 'a box to be selected'
Start-Sleep 1
Save-Shot $slug '02-box' -Callouts $title, (Find-Ui -Id ParamsHeader), (Find-Ui -Id PinsHeader), (Find-Ui -Id FindStatus) -Region (Find-Ui -Id SideTabs), $find

Select-Ui (Find-Ui -Id LuaTab)
Start-Sleep 2
Save-Shot $slug '03-lua' -Callouts (Find-Ui -Id LuaTab) -Region (Find-Ui -Id SideTabs)

Stop-ShotApp
