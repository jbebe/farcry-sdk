# Replace music and speech: the English main-menu theme out as MP3, a generated 30 s chord in.
$slug = 'music'
$inputs = Join-Path (Get-ShotPaths).Bin 'inputs'
$clip = Join-Path $inputs 'menu-theme.ogg'
& (Get-ShotPaths).Ffmpeg -y -v error -f lavfi -i "aevalsrc='0.2*sin(2*PI*220*t)+0.2*sin(2*PI*277*t)+0.2*sin(2*PI*330*t)':d=30:s=48000" -ac 2 -c:a libvorbis -q:a 4 $clip
$mp3 = Join-Path $inputs 'menu-theme-original.mp3'
if (Test-Path $mp3) { Remove-Item $mp3 }

Reset-ShotState
Start-ShotApp -Width 1440 -Height 900 | Out-Null
Select-UiTab 'Files'
Set-UiValue (Find-Ui -Scope (Find-Ui -Id FilesTabItem) -Type Edit) 'ext:sbao 004b177b'
$grid = Find-Ui -Id FileGrid
Click-Ui (Select-UiRow '*\004b177b.sbao' $grid)
$frame = Get-DetailsFrame '004b177b.sbao'
$format = Find-Ui -Id ExportFormatCombo
Save-Shot $slug '01-preview' -Callouts (Find-Ui -Id PlayButton), $format, (Find-Ui -Id ExportButton), (Find-Ui -Id ImportButton) -Region $frame

Select-UiCombo $format 'MP3*'
Invoke-Ui (Find-Ui -Id ExportButton)
Complete-FileDialog 'Export audio' $mp3
Wait-Ui { (Test-Path $mp3) -and (Get-Item $mp3).Length -gt 100KB } -Timeout 120 -What 'the MP3 export' | Out-Null

Invoke-Ui (Find-Ui -Id ImportButton)
Complete-FileDialog 'Import replacement audio*' $clip
$staged = Join-Path (Get-ShotPaths).App 'workspace\mods\soundbinary\004b177b.sbao'
Wait-Ui { Test-Path $staged } -Timeout 120 -What 'the music to be staged' | Out-Null
Click-Ui (Select-UiRow '*\004b177b.sbao' $grid)
$frame = Get-DetailsFrame '004b177b.sbao'
Save-Shot $slug '02-staged' -Callouts (Find-Ui -Type Text -Like 'Mod: workspace*'), (Find-Ui -Id StatusText), (Find-UiButton 'Revert') -Region $frame

Select-UiTab 'Mods'
Invoke-Ui (Find-UiButton 'Deploy mods')
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-ShotResult $slug
Stop-ShotApp
