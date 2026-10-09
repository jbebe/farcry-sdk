# Managing mods: load order, turning mods on and off, and two mods that change the same value.
$slug = 'managing-mods'
$inputs = Join-Path (Get-ShotPaths).Bin 'inputs'

# Two one-file mods from the first-mod result: the world1 AK-47 magazine at 40 and at 50 rounds.
$source = Join-Path (Get-ShotPaths).Bin 'results\first-mod\workspace\mods\worlds\world1\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml'
$zips = foreach ($n in 40, 50) {
    $layer = Join-Path $inputs "layers\ak47-$n-rounds"
    $file = Join-Path $layer 'mods\worlds\world1\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml'
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    $xml = [IO.File]::ReadAllText($source).Replace('iAmmoInClip" type="Int32">60<', "iAmmoInClip`" type=`"Int32`">$n<")
    [IO.File]::WriteAllText($file, $xml, (New-Object Text.UTF8Encoding $false))
    New-ShotZip "ak47-$n-rounds.zip" $layer
}
$vss = 'C:\Projects\FarCry2\mods\vss-vintorez\vss-vintorez-1.1.0.zip'

Reset-ShotState -Mods (@($vss) + $zips)
Start-ShotApp | Out-Null

$grid = Find-Ui -Id ModGrid
$row = Find-UiByText -Scope $grid -Type DataItem -Like 'ak47-40*'
Select-Ui $row
Start-Sleep -Milliseconds 400
$arrows = @(Find-Ui -Scope $row -Type Button -All)
Save-Shot $slug '01-mods' -Callouts $arrows[0], (Find-Ui -Scope $row -Type CheckBox), (Find-Ui -Type Button -Name 'Open location'), (Find-Ui -Type Button -Name 'Remove'), (Find-Ui -Type Button -Name 'Rescan mods')

$deploy = Find-Ui -Type Button -Name 'Deploy mods'
Invoke-Ui $deploy
$failed = Wait-Ui { Find-ShotWindow 'Build failed' } -Timeout 120 -What 'the conflict to fail the build'
if ((Get-DialogText $failed) -notmatch 'iAmmoInClip') { throw 'The build failed for another reason' }
Save-Shot $slug '02-conflict' -Window $failed -Screen
Push-ShotButton $failed 'OK'

$row50 = Find-UiByText -Scope $grid -Type DataItem -Like 'ak47-50*'
$active = Find-Ui -Scope $row50 -Type CheckBox
Set-UiToggle $active $false
Start-Sleep 2
Invoke-Ui $deploy
Wait-ShotStatus 'Built patch.dat*' | Out-Null
Add-SessionPatch
Save-Shot $slug '03-one-off' -Callouts $active, $deploy, (Get-ShotStatusElement)

Stop-ShotApp
