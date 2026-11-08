<#
.SYNOPSIS
    Sets up the GOG install for a mod performance test, switches it between the two states, collects
    DevTools' session logs, and puts the install back.

.DESCRIPTION
    reset    backs up the patch, bin\plugins and the Session log setting, deploys DevTools and UFCP (and
             -With) alone, removes every plugin no layer deploys except DevTools.dll, and turns the
             Session log on. Starts a run folder under "C:\Games\Far Cry 2 - perftest". -Length is one
             session's play in seconds, which compare.py scales its blocks to.
    mod      deploys the same plus -Mod.
    switch   puts a state deployed earlier back by copying it: -State baseline or mod.
    collect  moves the session logs written since the current state was deployed into the run folder,
             as <state>-<n>.csv, and says how much play each holds.
    restore  puts back the patch, bin\plugins and the Session log setting from before the reset.

    Each step but reset works on the newest run folder unless -Run names one. Builds are not done
    here: every layer named must be current already. Reset refuses while the newest run is not
    restored, since it would back up that run's test setup as the user's own; -Force backs it up anyway.
#>
param(
    [Parameter(Mandatory)][ValidateSet('reset', 'mod', 'switch', 'collect', 'restore')][string]$Step,
    [string]$Mod,
    [string[]]$With = @(),
    [int]$Length = 30,
    [ValidateSet('baseline', 'mod')][string]$State,
    [string]$Run,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$Game = 'C:\Games\Far Cry 2'
$Bin = "$Game\bin"
$Plugins = "$Bin\plugins"
$Data = "$Game\Data_Win32"
$Root = "$Game - perftest"
$Repo = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$Base = @('DevTools', 'UFCP')

# The CLI reports progress on stderr, which Windows PowerShell would otherwise stop the script on.
function Invoke-Cli {
    $cli = Get-ChildItem "$Repo\tools\JackAll\src\JackAll.Cli\bin\*\net10.0\jackall-cli.exe" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $cli) { throw 'jackall-cli is not built: dotnet build tools\JackAll\src\JackAll.Cli -c Release' }
    $ErrorActionPreference = 'Continue'
    & $cli.FullName @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "jackall-cli $($args[0]) $($args[1]) failed" }
}

function Assert-GameClosed {
    if (Get-Process farcry2, FCSE -ErrorAction SilentlyContinue) { throw 'The game is running: quit it first.' }
}

function Read-RunInfo($folder) { Get-Content "$folder\run.json" -Raw | ConvertFrom-Json }
function Save-RunInfo($folder, $info) { $info | ConvertTo-Json | Set-Content "$folder\run.json" -Encoding utf8 }

function Find-Run {
    if ($Run) { return (Resolve-Path $Run).Path }
    $newest = Get-ChildItem $Root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $newest) { throw "No run folder under $Root - start with -Step reset." }
    $newest.FullName
}

# fcse.ini as lines, kept CRLF and without a BOM when written back.
function Get-IniValue($section, $key) {
    $inSection = $false
    foreach ($line in [IO.File]::ReadAllLines("$Bin\fcse.ini")) {
        if ($line -match '^\[(.+)\]$') { $inSection = $Matches[1] -eq $section; continue }
        if ($inSection -and $line -match "^$([regex]::Escape($key))\s*=\s*(.*)$") { return $Matches[1] }
    }
    $null
}

function Set-IniValue($section, $key, $value) {
    $lines = [Collections.Generic.List[string]][IO.File]::ReadAllLines("$Bin\fcse.ini")
    $start = $lines.IndexOf("[$section]")
    if ($start -lt 0) { throw "fcse.ini has no [$section] section - launch the game once with DevTools installed." }
    $end = $start + 1
    while ($end -lt $lines.Count -and $lines[$end] -notmatch '^\[') { $end++ }
    $at = -1
    for ($i = $start + 1; $i -lt $end; $i++) { if ($lines[$i] -match "^$([regex]::Escape($key))\s*=") { $at = $i } }
    if ($at -ge 0) {
        $lines[$at] = "$key = $value"
    } else {
        $last = $end - 1
        while ($last -gt $start -and $lines[$last].Trim() -eq '') { $last-- }
        $lines.Insert($last + 1, "$key = $value")
    }
    [IO.File]::WriteAllText("$Bin\fcse.ini", ($lines -join "`r`n") + "`r`n")
}

function Copy-Install($to) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Copy-Item "$Data\patch.fat", "$Data\patch.dat" $to
    Copy-Item $Plugins "$to\plugins" -Recurse
}

function Restore-Install($from) {
    Copy-Item "$from\patch.fat", "$from\patch.dat" $Data
    Get-ChildItem $Plugins -Force | Remove-Item -Recurse -Force
    Copy-Item "$from\plugins\*" $Plugins -Recurse -Force
}

function Deploy($folder, $info, $name, [string[]]$mods) {
    $layers = foreach ($m in $mods) {
        $path = "$Repo\mods\$m\layer"
        if (-not (Test-Path $path)) { throw "mods\$m has no layer\ - build it first." }
        '--layer'; $path
    }
    Invoke-Cli mod build --game $Game @layers | Select-Object -Last 3

    # Whatever no layer deployed, other than DevTools.dll, is not part of the test; the backup holds it.
    $keep = @((Get-Content "$Plugins\.jackall-plugins.json" -Raw | ConvertFrom-Json).files.path) +
        @('DevTools.dll', '.jackall-plugins.json')
    foreach ($file in Get-ChildItem $Plugins -Recurse -File -Force) {
        if ($keep -notcontains $file.FullName.Substring($Plugins.Length + 1)) { Remove-Item $file.FullName -Force }
    }
    Get-ChildItem $Plugins -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Force) } | Remove-Item

    Copy-Install "$folder\state-$name"
    $info.current = $name
    $info.deployed | Add-Member -NotePropertyName $name -NotePropertyValue (Get-Date).ToString('o') -Force
    Save-RunInfo $folder $info
    "deployed $name : layers $($mods -join ', ')"
    'plugins: ' + ((Get-ChildItem $Plugins -Recurse -File -Filter *.dll | ForEach-Object Name) -join ', ')
}

# What would keep frame time from showing a cost.
function Show-Settings {
    'UFCP Maximum frame rate: ' + (Get-IniValue 'UFCP' 'Maximum frame rate')
    $gamer = "$([Environment]::GetFolderPath('MyDocuments'))\My Games\Far Cry 2\GamerProfile.xml"
    if ((Get-Content $gamer -Raw) -match '<RenderProfile[^>]*\sVSync="(\d)"') { "VSync: $($Matches[1])" }
}

switch ($Step) {
    'reset' {
        if (-not $Mod) { throw '-Mod names the mod to test, as its folder under mods\.' }
        Assert-GameClosed
        if (-not (Test-Path "$Bin\FCSE.exe")) { throw "FCSE.exe is not in $Bin - install FCSE first." }
        $dll = "$Repo\mods\DevTools\out\build\x86-release\DevTools.dll"
        if (-not (Test-Path $dll)) { throw 'DevTools is not built: mods\DevTools\build.ps1' }
        $last = Get-ChildItem $Root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
            Select-Object -First 1
        if ($last -and -not $Force -and -not (Read-RunInfo $last.FullName).restored) {
            throw "The last run, $($last.FullName), was never restored: -Step restore puts the install back first, or -Force backs up the install as it is."
        }

        $folder = Join-Path $Root ((Get-Date).ToString('yyyy-MM-dd-HHmm') + "-$Mod")
        Copy-Install "$folder\before"
        $info = [pscustomobject]@{
            mod = $Mod; with = $With; length = $Length; current = $null; deployed = [pscustomobject]@{}
            restored = $false
            sessionLogBefore = Get-IniValue 'DevTools' 'Session log'
        }
        Save-RunInfo $folder $info
        "backed up to $folder\before"

        Copy-Item $dll "$Plugins\DevTools.dll" -Force
        Deploy $folder $info 'baseline' ($Base + $With)
        Set-IniValue 'DevTools' 'Session log' 'true'
        Show-Settings
        "run folder: $folder"
    }
    'mod' {
        Assert-GameClosed
        $folder = Find-Run
        $info = Read-RunInfo $folder
        Deploy $folder $info 'mod' ($Base + @($info.with | Where-Object { $_ }) + $info.mod)
        Show-Settings
    }
    'switch' {
        if (-not $State) { throw '-State baseline or mod.' }
        Assert-GameClosed
        $folder = Find-Run
        $info = Read-RunInfo $folder
        if (-not (Test-Path "$folder\state-$State")) { throw "state $State was never deployed in this run." }
        Restore-Install "$folder\state-$State"
        $info.current = $State
        $info.deployed | Add-Member -NotePropertyName $State -NotePropertyValue (Get-Date).ToString('o') -Force
        Save-RunInfo $folder $info
        "switched to $State"
    }
    'collect' {
        $folder = Find-Run
        $info = Read-RunInfo $folder
        $since = [datetime]$info.deployed.($info.current)
        $logs = Get-ChildItem "$Bin\DevTools-session-*.csv" -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationTime -gt $since } | Sort-Object CreationTime
        if (-not $logs) { "no session log since $($info.current) was deployed at $since" }
        foreach ($log in $logs) {
            $n = 1
            while (Test-Path "$folder\$($info.current)-$n.csv") { $n++ }
            $to = "$folder\$($info.current)-$n.csv"
            Move-Item $log.FullName $to
            $play = 0.0; $frames = 0
            foreach ($line in [IO.File]::ReadLines($to)) {
                $cells = $line.Split(',')
                if ($cells.Count -gt 3 -and $cells[3] -eq '1') { $play += [double]$cells[1]; $frames++ }
            }
            $line = '{0}: {1:N0} s in play, {2:N0} frames' -f (Split-Path $to -Leaf), ($play / 1000), $frames
            if ($play / 1000 -lt 0.75 * $info.length) { $line += "  <- short: this run was given $($info.length) s" }
            $line
        }
    }
    'restore' {
        Assert-GameClosed
        $folder = Find-Run
        $info = Read-RunInfo $folder
        Restore-Install "$folder\before"
        Set-IniValue 'DevTools' 'Session log' $(if ($info.sessionLogBefore) { $info.sessionLogBefore } else { 'false' })
        $info.restored = $true
        Save-RunInfo $folder $info
        "restored the install from $folder\before"
    }
}
