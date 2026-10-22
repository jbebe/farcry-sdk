# Builds new worlds out of tmpla, the editor's flat template world. Dot-source it, then call
# Build-TmplaWorld. It reads the extracted retail worlds archive in tmp\gamefiles\worlds\worlds.
$ErrorActionPreference = 'Stop'

$Retail = "$PSScriptRoot\..\tmp\gamefiles\worlds\worlds"
$Cli = "$PSScriptRoot\..\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) 'tmpla-world.xml'

$utf8 = New-Object Text.UTF8Encoding $false
$tmplaSide = 8
$sectorSize = 64

function Copy-To([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item $from $to
}

function Replace-Once([string]$text, [string]$old, [string]$new) {
    $at = $text.IndexOf($old)
    if ($at -lt 0 -or $text.IndexOf($old, $at + 1) -ge 0) { throw "expected exactly one '$old'" }
    $text.Replace($old, $new)
}

function Read-Fcb([string]$from) {
    & $Cli fcb decode $from -o $work | Out-Null
    $doc = New-Object Xml.XmlDocument
    $doc.Load($work)
    Remove-Item $work
    $doc
}

function Write-Fcb([Xml.XmlDocument]$doc, [string]$to) {
    $settings = New-Object Xml.XmlWriterSettings -Property @{ Indent = $true; Encoding = $utf8; OmitXmlDeclaration = $true }
    $writer = [Xml.XmlWriter]::Create($work, $settings)
    $doc.Save($writer)
    $writer.Close()
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    & $Cli fcb encode $work -o $to | Out-Null
    Remove-Item $work
}

# Sets an entity's hidPos and hidPos_precise.
function Set-Pos([Xml.XmlElement]$entity, [double]$x, [double]$y, [double]$z) {
    foreach ($pos in $entity.SelectNodes('value[@name="hidPos" or @name="hidPos_precise"]')) {
        $pos.SelectSingleNode('x').InnerText = "$x"
        $pos.SelectSingleNode('y').InnerText = "$y"
        $pos.SelectSingleNode('z').InnerText = "$z"
    }
}

# Decodes a retail .fcb, lets $edit change its XML, and encodes the result to $to.
function Edit-Fcb([string]$from, [string]$to, [scriptblock]$edit) {
    $doc = Read-Fcb $from
    & $edit $doc
    Write-Fcb $doc $to
}

# Writes worlds\$name and levels\$name under $layer from the first $side x $side sectors of tmpla.
# $spawn, when given, is the x, y, z SpawnPoint_0 is moved to.
function Build-TmplaWorld([string]$layer, [string]$name, [int]$side, [double[]]$spawn) {
    $srcWorld = "$Retail\worlds\tmpla"
    $srcLevel = "$Retail\levels\tmpla"
    $world = "$layer\worlds\$name"
    $gen = "$world\generated"
    $level = "$layer\levels\$name"
    $sectors = foreach ($y in 0..($side - 1)) { foreach ($x in 0..($side - 1)) { $x + $tmplaSide * $y } }

    foreach ($file in 'mapcompass.xbt', 'mapcompass_mip0.xbt', 'generated\entitylibrary.fcb', 'generated\moviedata.xml',
        'generated\tmpla.managers.fcb', 'generated\tmpla.omnis.fcb', 'generated\tmpla_depload.dat', 'generated\tmpla_deploadnewparticles.rml') {
        Copy-To "$srcWorld\$file" "$world\$($file -replace 'tmpla', $name)"
    }

    $xml = [IO.File]::ReadAllText("$srcWorld\generated\tmpla.game.xml")
    $xml = Replace-Once $xml 'Levels\tmpla\tmpla.nomad' "Levels\$name\$name.nomad"
    $xml = $xml.Replace("CountX=`"$tmplaSide`" CountY=`"$tmplaSide`"", "CountX=`"$side`" CountY=`"$side`"")
    [IO.File]::WriteAllText("$gen\$name.game.xml", $xml, $utf8)

    Edit-Fcb "$srcWorld\generated\tmpla.mapsdata.fcb" "$gen\$name.mapsdata.fcb" {
        param($doc)
        $doc.SelectSingleNode('/object/object[@type="tmpla"]').SetAttribute('type', $name)
        if ($spawn) {
            Set-Pos $doc.SelectSingleNode('//object[@type="Entity"][value[@name="hidName"]="SpawnPoint_0"]') @spawn
        }
    }
    Edit-Fcb "$srcWorld\generated\tmpla.sectorsdep.fcb" "$gen\$name.sectorsdep.fcb" {
        param($doc)
        $map = $doc.SelectSingleNode('/object/object[@type="tmpla"]')
        $map.SetAttribute('type', $name)
        foreach ($sector in @($map.SelectNodes('object[@type="CWorldSector"]'))) {
            if ([int]$sector.SelectSingleNode('value[@name="SectorId"]').InnerText -notin $sectors) { [void]$map.RemoveChild($sector) }
        }
    }

    # The level header is a grid and extent declaration followed by an all-zero per-sector table.
    $nvm = [IO.File]::ReadAllBytes("$srcWorld\generated\nv\sectors\nv.nvm")
    $head = [byte[]]$nvm[0..0x4F]
    foreach ($at in 0x18, 0x1C, 0x34, 0x38) { [BitConverter]::GetBytes([single]($sectorSize * $side)).CopyTo($head, $at) }
    foreach ($at in 0x40, 0x44) { [BitConverter]::GetBytes([int]$side).CopyTo($head, $at) }
    [BitConverter]::GetBytes([int]($side * $side)).CopyTo($head, 0x4C)
    New-Item -ItemType Directory -Force "$gen\nv\sectors" | Out-Null
    [IO.File]::WriteAllBytes("$gen\nv\sectors\nv.nvm", [byte[]]($head + (New-Object byte[] (4 * $side * $side)) + $nvm[($nvm.Length - 24)..($nvm.Length - 1)]))

    foreach ($file in 'mapcompass.xbt', 'mapcompass_mip0.xbt', 'generated\moviedata.xml') { Copy-To "$srcLevel\$file" "$level\$file" }
    # One atlas covers a 2 x 2 block of sectors and is named after the block's first sector.
    $atlases = $sectors | ForEach-Object { $_ - $_ % 2 - $tmplaSide * ([Math]::Floor($_ / $tmplaSide) % 2) } | Sort-Object -Unique
    $files = @(foreach ($id in $sectors) { "sdat\sd$id.sdat", "sdat\sd${id}_shadow.xbt", "worldsectors\sector$id.srl", "worldsectors\zonesector$id.zsr" }) +
        @(foreach ($id in $atlases) { 'color', 'diffuse', 'mask' | ForEach-Object { "sdat\atlas${id}_$_.xbt" } })
    foreach ($file in $files) { Copy-To "$srcLevel\generated\$file" "$level\generated\$file" }

    foreach ($id in $sectors) {
        $desc = "worldsectors\sector$id.desc.fcb"
        if ($side -eq $tmplaSide) { Copy-To "$srcLevel\generated\$desc" "$level\generated\$desc"; continue }
        Edit-Fcb "$srcLevel\generated\$desc" "$level\generated\$desc" {
            param($doc)
            $list = $doc.SelectSingleNode('/object/object[@type="SectorDesc"]')
            foreach ($other in @($list.SelectNodes('object[@type="Sector"]'))) {
                if ([int]$other.SelectSingleNode('value[@hash="BF396750"]').InnerText -notin $sectors) { [void]$list.RemoveChild($other) }
            }
        }
    }
}
