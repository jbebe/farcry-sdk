# The format commands: one worked chain per file family, written to bin\transcripts. Nothing here
# writes to the game; the commands run in bin\inputs\formats so the transcript shows short paths.
$slug = 'cli-formats'
$paths = Get-ShotPaths
$game = $paths.Game
$data = Join-Path $game 'Data_Win32'
$dir = Join-Path $paths.Bin 'inputs\formats'
if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item (Join-Path $paths.Bin 'inputs\ak47.fc2model'), (Join-Path $paths.Bin 'inputs\ak47-longer-mag.fc2model') $dir

function Step([string[]]$Arguments) {
    if (Invoke-ShotCli $Arguments) { throw "Failed: jackall-cli $($Arguments -join ' ')" }
}

# Pulls more inputs out of an archive without adding them to the transcript.
function Get-GameFile([string]$Archive, [string[]]$Filters) {
    $ErrorActionPreference = 'Continue'
    foreach ($f in $Filters) { & $paths.Cli archive extract "$data\$Archive" --names --filter $f --out-dir game 2>$null | Out-Null }
}

Push-Location $dir
try {
    # Archives
    Step 'archive', 'extract', "$data\worlds\worlds.fat", '--names', '--filter', 'ak47.xbg', '--out-dir', 'game'
    Get-GameFile 'worlds\worlds.fat' 'ak47_state01_m', 'mercbrain.ai.rml', '004bf5e9.spk', 'world1_depload.dat', 'w1_c_3\generated\worldsectors\worldsector3160.data.fcb'
    Get-GameFile 'common.fat' 'ak47_state01_m', 'pc\eng\ui\hud.mgb', 'english\oasisstrings.rml', 'movemgr.bin', 'a1sm01_townescape.a1sm01_mission'
    Get-GameFile 'sound.fat' '004b177b.sbao'
    $files = Get-ChildItem game -Recurse -File | ForEach-Object { $_.FullName.Substring($dir.Length + 1) }
    Write-Host ($files -join "`n")
    $find = { param($Name) $files | Where-Object { $_ -like "*\$Name" } | Select-Object -First 1 }

    # Models
    Step 'xbg', 'export', (& $find 'ak47.xbg'), '--out', 'ak47.obj'
    Step 'fc2model', 'inspect', 'ak47-longer-mag.fc2model'
    Step 'fc2model', 'extract', 'ak47-longer-mag.fc2model', '--out', 'ak47-longer-mag'

    # Textures
    $xbt = & $find 'ak47_state01_m.xbt'
    Step 'xbt', 'extract', $xbt, '--out-dir', 'texture'
    Step 'xbt', 'build', 'texture\ak47_state01_m.dds', '--out', 'ak47_state01_m.xbt'

    # Music and sound banks
    Step 'sbao', 'extract', (& $find '004b177b.sbao'), '--out-dir', 'music'
    Step 'sbao', 'build', 'music\004b177b.ogg', '--out', '004b177b.sbao'
    Step 'spk', 'list', (& $find '004bf5e9.spk')
    Step 'spk', 'decode', (& $find '004bf5e9.spk'), '--out', 'bank'
    Step 'spk', 'encode', 'bank\004bf5e9.xml', '--out', '004bf5e9.spk'
    Step 'spk', 'verify', '004bf5e9.spk'

    # Game data and strings
    $sector = & $find 'worldsector3160.data.fcb'
    Step 'fcb', 'decode', $sector, '--out', 'worldsector3160.xml'
    Step 'fcb', 'encode', 'worldsector3160.xml', '--out', 'worldsector3160.data.fcb'
    Step 'rml', 'decode', (& $find 'oasisstrings.rml'), '--out', 'oasisstrings.xml'
    Invoke-ShotCli 'rml', 'fragments', 'oasisstrings.rml' | Out-Null

    # Menus
    Step 'mgb', 'decode', (& $find 'hud.mgb'), '--out', 'hud.xml'
    Step 'mgb', 'verify', 'hud.xml'
    Step 'mgb', 'fragments', 'hud.xml', '--base', (& $find 'hud.mgb'), '--list'

    # Animations
    $move = & $find 'movemgr.bin'
    Step 'move', 'clips', $move, '--weapon', '2', '--shared-only'
    Step 'move', 'validate', $move
    Step 'move', 'hash', 'graphics\characters\_common\animations\weapons\primary\ak47\1stge_uppb_reload_+000fw_prak4_i1.mab'

    # Dependencies, brains, missions
    Step 'depload', 'validate', (& $find 'world1_depload.dat')
    Step 'ai', 'lint', (& $find 'mercbrain.ai.rml')
    Invoke-ShotCli 'domino', 'check', (& $find 'a1sm01_townescape.a1sm01_mission.lua') | Out-Null

    # References
    Step 'xref', 'build', '--game', $game
    Step 'xref', 'to', 'graphics\weapons\primary\ak47\ak47_state01_m.xbt', '--game', $game
    Step 'xref', 'from', 'graphics\_materials\sdore2-m-2008050631030648.xbm', '--game', $game
}
finally {
    Pop-Location
}
Save-ShotTranscript $slug
