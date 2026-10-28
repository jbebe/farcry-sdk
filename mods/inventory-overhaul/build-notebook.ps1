<#
.SYNOPSIS
    Builds the notebook gadget into the layer: its pages, the arms that hold it, and its archetype.

.DESCRIPTION
    The scripts in notebook\ do the work, each saying what it makes. Reads the map's model and banks
    from the install and everything else from the retail files under tmp\; writes layer\mods.
#>
param(
    [string]$Game = 'C:\Games\Far Cry 2',
    [string]$Blender = 'C:\Programs\Blender 5.2\blender.exe'
)
$ErrorActionPreference = "Stop"

$Repo = Resolve-Path "$PSScriptRoot\..\.."
$Cli = "$Repo\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
$Retail = "$Repo\tmp\gamefiles"
$Scripts = "$PSScriptRoot\notebook"
$Layer = "$PSScriptRoot\layer\mods"
$Work = "$PSScriptRoot\out\notebook"
$ModelFolder = "graphics\objects\equipment\notebook"
$Worlds = "world1", "world2"

# The map's banks on foot, which the notebook's arms are made from.
$BankFolder = "graphics/characters/_common/animations/equipment/equipped_gadget"
$Banks = "aimcyclecrh_+000fw_nowep_eqmpcp_i1", "aimoffset_+090rt_nowep_i1_eqmpcp",
    "aimoffset_-090lt_nowep_i1_eqmpcp", "aimoffsetcrh_+090rt_nowep_i1_eqmpcp",
    "aimoffsetcrh_-090lt_nowep_i1_eqmpcp", "draw_+000fw_nowep_eqmpcp_i1", "holster_+000fw_nowep_eqmpcp_i1",
    "idle_+000fw_nowep_eqmpcp_i1", "jumpend_+000fw_nowep_i1_eqmpcp", "jumpidle_+000fw_nowep_i1_eqmpcp",
    "jumpstart_+000fw_nowep_i1_eqmpcp", "run_+000fw_nowep_eqmpcp_i1", "slideend_+000fw_nowep_i1_eqmpcp",
    "slidepose_+000fw_nowep_i1_eqmpcp", "slidestart_+000fw_nowep_i1_eqmpcp", "walk_+000fw_nowep_i1_eqmpcp",
    "walkcrh_+000fw_nowep_i1_eqmpcp"

# Runs a program and returns its output, throwing when it fails. The CLI reports progress on stderr,
# which Windows PowerShell would otherwise stop the script on.
function Exec([string]$program) {
    $ErrorActionPreference = "Continue"
    $output = & $program @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 20 | Write-Host
        throw "$(Split-Path -Leaf $program) $($args[0]) failed"
    }
    $output
}

if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $Work | Out-Null
$built = @("$Layer\$ModelFolder") +
    ($Worlds | ForEach-Object { "$Layer\worlds\$_\generated\entitylibrary.fcb\gadgets\Equipped\Notebook.xml" })
foreach ($dir in $built) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}

# The pages, in a copy of the map's pack with its banks and a body to hold them, drawn with a copy of
# MAPTOP cut out by its alpha.
$clips = $Banks | ForEach-Object { "--clip"; "$BankFolder/1stge_uppb_$_.mab" }
Exec $Cli fc2model export -g $Game graphics/objects/mapcompass/map.xbg @clips -o "$Work\map.fc2model" | Out-Null
Exec python "$Scripts\page.py" "$Work\map.fc2model" "$Work\notebook.fc2model" "$Work\nodes.json"
Exec $Cli fc2model add-material "$Work\notebook.fc2model" "$ModelFolder\notebook_page.xbm" `
    --from "GRAPHICS\_MATERIALS\JFCARRIER2-M-1708200746783136.xbm" --name MAPTOP | Out-Null

# The arms. Blender exits 0 when the script fails, so only the pack it writes last tells.
Exec $Blender -b --python "$Scripts\arms.py" '--' "$Work\notebook.fc2model" "$Work\arms.fc2model" |
    Select-String "frames|Error|Traceback" | ForEach-Object { $_.Line }
if (-not (Test-Path "$Work\arms.fc2model")) { throw "arms.py failed" }
Exec $Cli fc2model extract "$Work\arms.fc2model" -o "$Work\extract" --all | Out-Null
New-Item -ItemType Directory -Force "$Layer\$ModelFolder" | Out-Null
foreach ($file in "notebook.xbg", "notebook_ref.skeleton", "notebook_page.xbm") {
    Copy-Item "$Work\extract\mods\$ModelFolder\$file" "$Layer\$ModelFolder\$file"
}
Exec $Cli xbm set "$Layer\$ModelFolder\notebook_page.xbm" AlphaTestEnabled=1 -g $Game | Out-Null
Exec python "$Scripts\clips.py" "$Work\extract" (Split-Path $Layer) $Work

# The archetype in each world, boxed by the pages' geometry.
Copy-Item "$Layer\$ModelFolder\notebook.xbg" "$Work\notebook.xbg"
Exec $Cli xbg export "$Work\notebook.xbg" | Out-Null
foreach ($world in $Worlds) {
    Copy-Item "$Retail\worlds\worlds\worlds\$world\generated\entitylibrary.fcb" "$Work\$world.entitylibrary.fcb"
    Exec $Cli fcb decode "$Work\$world.entitylibrary.fcb" | Out-Null
    Exec python "$Scripts\archetype.py" "$Work\$world.entitylibrary.xml" "$Work\notebook.obj" "$Work\nodes.json" `
        $Layer $world
}
