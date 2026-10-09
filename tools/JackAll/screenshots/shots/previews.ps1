# Looking inside any file: one capture of the details pane per kind of preview.
$slug = 'previews'

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900 | Out-Null
Select-UiTab 'Files'
$filter = Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit
$grid = Find-Ui -Id FileGrid

# Selects one file by its path and returns what frames the details pane: its heading and the note
# at the bottom.
function Open-Preview([string]$Query, [string]$PathLike) {
    Set-UiValue $filter $Query
    # An exact path is looked up by the list itself, which also finds rows scrolled out of view.
    if ($PathLike.Contains('*')) { $row = Select-UiRow $PathLike $grid } else { $row = Wait-Ui { $r = Get-UiItem $grid $PathLike; Select-Ui $r; $r } -Timeout 30 -What $PathLike; Start-Sleep -Milliseconds 300 }
    Click-Ui $row
    Start-Sleep -Milliseconds 800
    $name = Split-Path $PathLike -Leaf
    $heading = Wait-Ui { @(Find-Ui -Type Text -Name $name -All -Optional) | Where-Object { $_.Current.BoundingRectangle.X -gt 880 } | Select-Object -First 1 } -What "the details of $name"
    $note = Find-Ui -Type Text -Like 'Replacing a file stages it*'
    return @($heading, $note)
}

$frame = Open-Preview 'hash:585ACE93' '*\ak47.xbg'
Save-Shot $slug '01-model' -Callouts (Find-Ui -Id LodCombo), (Find-Ui -Id ResetViewButton) -Region $frame

$frame = Open-Preview 'ext:xbm 2007073066669286' '*\sdore2-m-2007073066669286.xbm'
Save-Shot $slug '02-material' -Region $frame

$frame = Open-Preview 'ext:xbt ak47_state01_m' '*\ak47_state01_m.xbt'
Save-Shot $slug '03-texture' -Callouts (Find-UiButton 'Export DDS + XML...'), (Find-UiButton 'Import DDS + XML...') -Region $frame

$frame = Open-Preview 'ext:mab 1stge_uppb_reload prak4' '*\1stge_uppb_reload_+000fw_prak4_i1.mab'
$play = Find-Ui -Id PlayToggle
Invoke-Ui $play
Start-Sleep -Milliseconds 1500
Invoke-Ui $play
Save-Shot $slug '04-animation' -Callouts $play, (Find-Ui -Id FrameSlider) -Region $frame

$frame = Open-Preview 'ext:sbao 004b177b' '*\004b177b.sbao'
Save-Shot $slug '05-music' -Callouts (Find-Ui -Id PlayButton), (Find-Ui -Id ExportFormatCombo), (Find-Ui -Id ExportButton), (Find-Ui -Id ImportButton) -Region $frame

$frame = Open-Preview 'ext:spk 004bf5e9' '*\004bf5e9.spk'
Save-Shot $slug '06-sound-bank' -Region $frame

$frame = Open-Preview 'ext:sdat sd100' 'levels\w1_e_2\generated\sdat\sd100.sdat'
Save-Shot $slug '07-terrain' -Callouts (Find-Ui -Type Image -Optional), (Find-UiButton 'Export as PNG...') -Region $frame

$frame = Open-Preview 'world1_depload.dat' 'worlds\world1\generated\world1_depload.dat'
Save-Shot $slug '08-depload' -Callouts (Find-Ui -Id HeaderText) -Region $frame

$frame = Open-Preview 'ext:mgb hud' 'ui\localized\pc\eng\ui\hud.mgb'
Save-Shot $slug '09-menu' -Callouts (Find-UiButton 'Open in MGB Editor...') -Region $frame

$frame = Open-Preview 'ext:lua a1sm01_mission' '*\a1sm01_townescape.a1sm01_mission.lua'
Save-Shot $slug '10-mission' -Callouts (Find-Ui -Id OpenButton) -Region $frame

$frame = Open-Preview 'gamemodesconfig' '*\gamemodesconfig.xml'
Save-Shot $slug '11-text' -Region $frame

$frame = Open-Preview 'ext:rtx hy_aloes_01' '*\hy_aloes_01.rtx'
Save-Shot $slug '12-no-preview' -Callouts (Find-Ui -Type Text -Like 'No preview available*') -Region $frame

Stop-ShotApp
