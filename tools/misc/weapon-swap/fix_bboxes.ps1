# Rewrite an entity fragment's baked bounding boxes from the pack actually being
# shipped. The archetype carries a description of the model; ship different
# geometry and those boxes still describe the donor, which can cull the weapon.
#
# Matches parts by NAME, never by index - the fragment lists them in the
# archetype's order and the pack in its own.
param(
  [Parameter(Mandatory)][string]$Pack,
  [Parameter(Mandatory)][string]$Fragment
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [IO.Compression.ZipFile]::OpenRead($Pack)
$sr  = New-Object IO.StreamReader($zip.GetEntry('model/mesh.json').Open())
$mesh = $sr.ReadToEnd() | ConvertFrom-Json; $sr.Close(); $zip.Dispose()

function N([double]$v) { '{0:0.######}' -f $v }
function Trip($a, $i) { (@(N $a[$i]; N $a[$i+1]; N $a[$i+2]) -join ',') }

# LOD0 bounds per part. bounds = [sphere xyzr][aabb min xyz][aabb max xyz]
$box = @{}
foreach ($p in $mesh.parts) {
  if ($p.name -notlike '*_LOD0') { continue }
  $box[($p.name -replace '_LOD0$','')] = @{ min = (Trip $p.bounds 4); max = (Trip $p.bounds 7) }
}

$lines = Get-Content $Fragment
$out = New-Object System.Collections.Generic.List[string]
$hits = 0
foreach ($line in $lines) {
  if ($line -match '<object index="\d+" meshName="([A-Za-z0-9_]+)"' -and $box.ContainsKey($Matches[1])) {
    $b = $box[$Matches[1]]
    $line = $line -replace 'bboxMin="[^"]*"', ('bboxMin="' + $b.min + '"')
    $line = $line -replace 'bboxMax="[^"]*"', ('bboxMax="' + $b.max + '"')
    $hits++
  }
  elseif ($line -match '<resource fileName="[^"]*\.xbg"') {
    $line = $line -replace 'bboxMin="[^"]*"', ('bboxMin="' + (Trip $mesh.box 0) + '"')
    $line = $line -replace 'bboxMax="[^"]*"', ('bboxMax="' + (Trip $mesh.box 3) + '"')
    $hits++
  }
  $out.Add($line)
}
if ($hits -ne ($box.Count + 1)) { throw "rewrote $hits boxes, expected $($box.Count + 1)" }
[IO.File]::WriteAllLines($Fragment, $out, (New-Object Text.UTF8Encoding($false)))
Write-Output ("  {0} boxes rewritten in {1}" -f $hits, (Split-Path $Fragment -Leaf))
foreach ($k in ($box.Keys | Sort-Object)) { Write-Output ("    {0,-12} {1}  ..  {2}" -f $k, $box[$k].min, $box[$k].max) }
Write-Output ("    {0,-12} {1}  ..  {2}" -f 'RESOURCE', (Trip $mesh.box 0), (Trip $mesh.box 3))
