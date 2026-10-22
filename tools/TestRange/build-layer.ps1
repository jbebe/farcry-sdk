# Builds layer\ from the extracted retail worlds archive: testrange, tmpla's flat plain with the spawn
# point in the middle and cover walls scattered around it.
. "$PSScriptRoot\..\..\scripts\tmpla-world.ps1"

$name = 'testrange'
$layer = "$PSScriptRoot\layer\mods"
$ground = 16.0625
$spawn = 256, 240, ($ground + 0.44)
$wallCount = 14
$firstId = 2099100000000000000

# Retail instances the walls are copied from: a 1.2 m barrier and a 1.8 m eroded wall.
$templates = @(
    @{ Sector = 'mp_16_airbase\generated\worldsectors\worldsector46.data.fcb'; Name = 'CoverObjects.WallBarrier01_BK.Multi_0' },
    @{ Sector = 'mp_07_m_dogon\generated\worldsectors\worldsector41.data.fcb'; Name = 'CoverObjects.WallEroded10_BK.Multi_0' }
) | ForEach-Object {
    (Read-Fcb "$Retail\levels\$($_.Sector)").SelectSingleNode("//object[@type='Entity'][value[@name='hidName']='$($_.Name)']")
}

if (Test-Path "$PSScriptRoot\layer") { Remove-Item -Recurse -Force "$PSScriptRoot\layer" }
Build-TmplaWorld $layer $name $tmplaSide $spawn

# Walls 12 to 90 m from the spawn point, at least 6 m apart, the same every build.
$random = New-Object Random 7
$walls = @()
while ($walls.Count -lt $wallCount) {
    $distance = 12 + $random.NextDouble() * 78
    $bearing = $random.NextDouble() * 2 * [Math]::PI
    $x = [Math]::Round($spawn[0] + $distance * [Math]::Cos($bearing), 2)
    $y = [Math]::Round($spawn[1] + $distance * [Math]::Sin($bearing), 2)
    $yaw = [Math]::Round($random.NextDouble() * 360, 1)
    if ($walls | Where-Object { ($_.X - $x) * ($_.X - $x) + ($_.Y - $y) * ($_.Y - $y) -lt 36 }) { continue }
    $walls += [pscustomobject]@{ X = $x; Y = $y; Yaw = $yaw; Index = $walls.Count }
}

$walls | Group-Object { [Math]::Floor($_.X / $sectorSize) + $tmplaSide * [Math]::Floor($_.Y / $sectorSize) } | ForEach-Object {
    $id = [int]$_.Name
    $doc = New-Object Xml.XmlDocument
    $doc.LoadXml(@"
<object type="WorldSector">
  <value name="Id" type="UInt32">$id</value>
  <value name="X" type="UInt32">$($id % $tmplaSide)</value>
  <value name="Y" type="UInt32">$([Math]::Floor($id / $tmplaSide))</value>
  <object type="MissionLayer">
    <value name="text_PathId" type="String">main</value>
    <value name="PathId" type="Hash">BF28CD64</value>
  </object>
</object>
"@)
    $main = $doc.SelectSingleNode('/object/object')
    foreach ($wall in $_.Group) {
        $entity = $doc.ImportNode($templates[$wall.Index % $templates.Count], $true)
        $entity.SelectSingleNode('value[@name="hidName"]').InnerText = "TestRange_Wall_$($wall.Index)"
        $entity.SelectSingleNode('value[@name="disEntityId"]').InnerText = "$($firstId + $wall.Index)"
        Set-Pos $entity $wall.X $wall.Y $ground
        $entity.SelectSingleNode('value[@name="hidAngles"]/z').InnerText = "$($wall.Yaw)"
        [void]$main.AppendChild($entity)
    }
    Write-Fcb $doc "$layer\levels\$name\generated\worldsectors\worldsector$id.data.fcb"
}

Write-Host "Wrote $PSScriptRoot\layer with $wallCount walls"
