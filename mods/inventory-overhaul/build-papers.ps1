<#
.SYNOPSIS
    Builds the papers lying in the world into the layer: each an archetype and an instance in its sector.

.DESCRIPTION
    notebook\papers.py does the work. Reads each world's entity library from the retail files under
    tmp\; writes layer\mods.
#>
$ErrorActionPreference = "Stop"

$Repo = Resolve-Path "$PSScriptRoot\..\.."
$Cli = "$Repo\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
$Layer = "$PSScriptRoot\layer\mods"
$Work = "$PSScriptRoot\out\papers"
# The worlds with papers in them.
$Worlds = "world1"

if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $Work | Out-Null
$built = @("$Layer\levels") + ($Worlds | ForEach-Object { "$Layer\worlds\$_\generated\entitylibrary.fcb\OA_MissionPickups" })
foreach ($dir in $built) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}

foreach ($world in $Worlds) {
    Copy-Item "$Repo\tmp\gamefiles\worlds\worlds\worlds\$world\generated\entitylibrary.fcb" "$Work\$world.entitylibrary.fcb"
    & $Cli fcb decode "$Work\$world.entitylibrary.fcb" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "fcb decode failed for $world" }
    python "$PSScriptRoot\notebook\papers.py" "$Work\$world.entitylibrary.xml" $Layer $world
    if ($LASTEXITCODE -ne 0) { throw "papers.py failed for $world" }
}
