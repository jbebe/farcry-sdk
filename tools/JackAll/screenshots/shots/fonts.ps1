# Add a font: Caveat drawn at weight 600 and 40 px with Make a font, staged as Caveat_40.
$slug = 'fonts'
$font = Join-Path (Get-ShotPaths).Repo 'mods\inventory-overhaul\notebook\fonts\Caveat-VariableFont_wght.ttf'

Reset-ShotState
Start-ShotApp -Width 1400 -Height 900
Select-UiTab 'Mods'
$make = Find-UiButton 'Make a font'
Start-Sleep 1
Save-Shot $slug '01-button' -Callouts $make -Region (Find-UiButton 'Import mod'), $make -Pad 20

# The wizard is modal, so it is clicked open rather than invoked.
Click-Ui $make
$wizard = Wait-Ui { Find-ShotWindow 'Make a font' } -What 'the font wizard'
Click-Ui (Find-Ui -Scope $wizard -Id FontWizardBrowse)
Complete-FileDialog 'Pick a font file' $font
Wait-Ui { (Get-UiValue (Find-Ui -Scope $wizard -Id FontWizardFile)) -like '*Caveat*' } -What 'the font file' | Out-Null
$face = Find-Ui -Scope $wizard -Id FontWizardFace
$size = Find-Ui -Scope $wizard -Id FontWizardSize
Set-UiRange $size 40
$weight = @(Find-Ui -Scope $wizard -Type Slider -All | Where-Object { $_.Current.AutomationId -ne 'FontWizardSize' })[0]
Set-UiRange $weight 600
$stage = Find-Ui -Scope $wizard -Id FontWizardStage
Wait-Ui { $stage.Current.IsEnabled } -Timeout 30 -What 'the preview' | Out-Null
Start-Sleep 1
Save-Shot $slug '02-wizard' -Window $wizard -Callouts $face, $weight, $size, (Find-Ui -Scope $wizard -Id FontWizardName), $stage

Click-Ui $stage
Wait-ShotStatus 'Staged Caveat_40*' | Out-Null
$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\ui\localized\pcwidescreen\eng\ui\fonts.mgb\_fonts.xml'
if (-not (Select-String -Path $staged -Pattern 'Caveat_40' -Quiet)) { throw "The font is not in $staged." }

# The staged font in the Files tab, set from its own page as the game sets it.
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'caveat'
$row = Wait-Ui { $r = Get-UiItem (Find-Ui -Id FileGrid) 'ui\fonts\caveat_40.mft'; Select-Ui $r; $r } -Timeout 30 -What 'caveat_40.mft'
Click-Ui $row
Start-Sleep -Milliseconds 800
Save-Shot $slug '03-preview' -Region (Get-DetailsFrame 'caveat_40.mft')

Save-ShotResult $slug
Stop-ShotApp
