# Containers and fragments: the first mod's AK-47, one piece of world1's entity library, on the
# Files tab.
$slug = 'fragments'

Reset-ShotState -Workspace 'first-mod'
Start-ShotApp -Width 1440 -Height 900

Select-UiTab 'Files'
Click-Ui (Open-FilesFolder 'worlds\world1\generated\entitylibrary.fcb\WeaponProperties\Primary') -X 0.3
$grid = Find-Ui -Id FileGrid
Click-Ui (Select-UiRow '*\ak47.xml' $grid)
$path = Find-Ui -Type Text -Like 'worlds\world1\generated\entitylibrary.fcb\*'
$note = Find-Ui -Type Text -Like 'This is one piece of a splitting .fcb*'
$diff = Find-Ui -Type Text -Like 'Showing only the changed lines*'
Save-Shot $slug '01-container' -Callouts (Select-UiRow '*\ak47.xml' $grid), $path, $note, $diff

Stop-ShotApp
