# What uses this file: the AK-47 model's references, following one, going back.
$slug = 'references'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900
Select-UiTab 'Files'
Click-Ui (Open-FilesFolder 'graphics\weapons\primary\ak47') -X 0.3
$grid = Find-Ui -Id FileGrid
Click-Ui (Find-UiByText -Scope $grid -Type DataItem -Like 'ak47.xbg*')

$incoming = Find-Ui -Id IncomingExpander
$outgoing = Find-Ui -Id OutgoingExpander
Wait-Ui { (Get-UiText $incoming) -match '\([1-9]' } -Timeout 900 -What 'the reference index' | Out-Null
Expand-Ui $incoming
Expand-Ui $outgoing
Start-Sleep 1
$material = @(Find-Ui -Scope (Find-Ui -Id OutgoingGrid) -Type DataItem -All)[0]
Save-Shot $slug '01-panel' -Callouts $incoming, $outgoing, $material

# Walk the incoming list with the keyboard to world1's entity library; the grid only realizes what's visible.
$users = Find-Ui -Id IncomingGrid
Click-Ui @(Find-Ui -Scope $users -Type DataItem -All)[0]
function Get-SelectedRow($Grid) {
    Find-Ui -Scope $Grid -Type DataItem -All -Optional |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
}
for ($i = 0; $i -lt 140 -and (Get-UiText (Get-SelectedRow $users)) -notlike 'worlds\world1\generated\entitylibrary.fcb*'; $i++) { Send-ShotKeys 'Down' }
$user = Get-SelectedRow $users
if ((Get-UiText $user) -notlike 'worlds\world1\generated\entitylibrary.fcb*') { throw "world1's library isn't among the model's users" }
Save-Shot $slug '02-user' -Callouts $user -Region $incoming, $outgoing

Click-Ui $user -Right
$menu = Wait-Ui { Find-ShotMenu } -What 'the reference menu'
Save-Shot $slug '03-menu' -Screen -NoPark -Callouts (Find-Ui -Scope $menu -Type MenuItem -Name 'Filter files by this hash') -Region $user, $menu
Send-ShotKeys 'Esc'

Send-ShotKeys 'Enter'
$path = Wait-Ui { Find-Ui -Type Text -Name 'worlds\world1\generated\entitylibrary.fcb' -Optional } -What 'the jump to the library'
$listed = Wait-Ui { Get-SelectedRow (Find-Ui -Id FileGrid) | Where-Object { (Get-UiText $_) -like 'entitylibrary.fcb*' } } -What 'the file list to follow the jump'
Save-Shot $slug '04-followed' -Callouts (Get-DetailsFrame 'entitylibrary.fcb')[0], $path, $listed

# From a focused row, where a window key binding never sees the key.
Click-Ui $listed -X 0.3
Send-ShotKeys 'Alt+Left'
Wait-Ui { Find-Ui -Type Text -Name 'graphics\weapons\primary\ak47\ak47.xbg' -Optional } -What 'Alt+Left to go back' | Out-Null

Stop-ShotApp
