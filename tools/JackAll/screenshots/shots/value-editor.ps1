# The value editor in depth, on the AK-47's weapons archetype: layout, field kinds, lists, the file
# picker, and a value that won't save.
$slug = 'value-editor'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900
Select-UiTab 'Archetypes'
$picker = Find-Ui -Id WorldPicker
Wait-Ui { $picker.Current.IsEnabled } -Timeout 60 -What 'the world picker' | Out-Null
Select-UiCombo $picker 'world1'
Invoke-Ui (Find-Ui -Id LoadButton)
Wait-Ui { (Find-Ui -Id StatusText).Current.Name -like '1,456 archetypes*' } -Timeout 300 -What 'the archetypes' | Out-Null
Set-UiValue (Find-Ui -Id SearchBox) 'AK47'
$tree = Find-Ui -Id ArchetypeTree
Expand-Ui (Find-UiByText -Scope $tree -Like 'weapons*')
Expand-Ui (Find-UiByText -Scope $tree -Like 'Primary*')
Select-Ui (Find-UiByText -Scope $tree -Like 'AK47')
Start-Sleep 1
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

$graphic = Find-UiByText -Type Group -Like 'CGraphicComponent*'
Save-Shot $slug '01-layout' -Callouts (Find-Ui -Id OutlineTree), (Find-Ui -Scope $graphic -Type Button -Name 'Remove'), (Find-Ui -Id ComponentToAdd), (Find-Ui -Type Button -Name 'Add component'), (Find-Ui -Type Button -Name 'Save')

Expand-Ui $graphic
Start-Sleep 1
$fileDesc = Open-UiSection 'CFileDescriptorComponent*' $null
$twin = Find-UiField 'text_fileName' $fileDesc
$model = Find-UiField 'fileName' $fileDesc
$pick = Find-Ui -Scope $walker.GetParent($model) -Id FcbPickFile
Show-Ui $twin
$twinLabel = Find-Ui -Scope $fileDesc -Type Text -Name 'text_fileName'
Save-Shot $slug '02-fields' -Callouts $twin, $model, $pick -Region $twinLabel, $twin, $pick -Pad 40

$list = Find-Ui -Scope $graphic -Type Button -Name '+ Add item'
Show-Ui $list
$listLabel = Find-Ui -Scope $graphic -Type Text -Name 'VisibilityNodes'
Save-Shot $slug '03-list' -Callouts $listLabel, $list -Region $listLabel, $list -Pad 60

$descriptor = Open-UiSection 'CSimpleAnimationComponent*' $null
$skeleton = Find-UiField 'fileSkeleton' $descriptor
Invoke-Ui (Find-Ui -Scope $walker.GetParent($skeleton) -Id FcbPickFile)
$window = Wait-Ui { Find-ShotWindow 'Pick a file' } -What 'the file picker'
$search = Find-Ui -Scope $window -Id FilePickerSearch
Set-UiValue $search 'ak47'
$hit = Find-UiByText -Scope (Find-Ui -Scope $window -Id FilePickerGrid) -Type DataItem -Like 'ak47_ref.skeleton*' -Timeout 30
Select-Ui $hit
Save-Shot $slug '04-picker' -Window $window -Callouts $search, (Find-Ui -Scope $window -Id FilePickerOnlyExtension -Optional), $hit, (Find-Ui -Scope $window -Id FilePickerOk)
Invoke-Ui (Find-Ui -Scope $window -Id FilePickerCancel)

$floatRow = @(Find-Ui -Scope $graphic -Type Text -All | Where-Object { $_.Current.Name -match '^f[A-Z]' })[0]
$float = Find-UiField $floatRow.Current.Name $graphic
Set-UiValue $float 'abc'
(Find-UiField 'bCastShadow' $graphic).SetFocus()
$warning = Wait-Ui { Find-Ui -Type Text -Like '*need fixing before saving*' -Optional } -What 'the validation message'
$save = Find-Ui -Type Button -Name 'Save'
if ($save.Current.IsEnabled) { throw 'Save stayed enabled with an invalid value' }
Save-Shot $slug '05-invalid' -Callouts $float, $warning, $save -Region $float, $save

Invoke-Ui (Find-UiFieldButton $floatRow.Current.Name 'Restore' $graphic)
Wait-Ui { -not (Find-Ui -Type Text -Like '*need fixing before saving*' -Optional) } -What 'Restore to clear the error' | Out-Null
Stop-ShotApp
