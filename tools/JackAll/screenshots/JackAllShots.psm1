# Drives a private JackAll build through UI Automation and writes annotated screenshots for the docs.
# Keep this file ASCII: Windows PowerShell 5.1 reads BOM-less scripts in the ANSI code page.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
# Without this, Windows PowerShell 5.1 writes some arrays to JSON as {"value": [...], "Count": n}.
Remove-TypeData System.Array -ErrorAction SilentlyContinue
if (-not ('ShotNative' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'Native.cs') -ReferencedAssemblies System.Drawing
}
[ShotNative]::Init()

$script:Game = if ($env:JACKALL_SHOTS_GAME) { $env:JACKALL_SHOTS_GAME } else { 'C:\Games\Far Cry 2' }
$script:Bin = Join-Path $PSScriptRoot 'bin'
$script:AppDir = Join-Path $script:Bin 'app'
$script:Cli = Join-Path $script:Bin 'cli\jackall-cli.exe'
$script:ImageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\docs\static\img\jackall'))
$script:SessionRoot = Join-Path $env:LOCALAPPDATA 'JackAllShots'
$script:SessionFile = Join-Path $script:SessionRoot 'session.json'
$script:Ffmpeg = if (Test-Path 'C:\Programs\ffmpeg\bin\ffmpeg.exe') { 'C:\Programs\ffmpeg\bin\ffmpeg.exe' } else { Join-Path $script:AppDir 'data\ffmpeg.exe' }
$script:Process = $null
$script:Window = $null

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$TS = [System.Windows.Automation.TreeScope]

function Get-ShotPaths {
    [pscustomobject]@{ Game = $script:Game; Bin = $script:Bin; App = $script:AppDir; Cli = $script:Cli; Images = $script:ImageRoot; Ffmpeg = $script:Ffmpeg }
}

# ---------------------------------------------------------------- session safety

# The files in the real install a session may change, relative to the game folder.
$script:Guarded = @('Data_Win32\patch.dat', 'Data_Win32\patch.fat', 'Data_Win32\patch.dat.vanilla', 'Data_Win32\patch.fat.vanilla', '.jackallcache')

function Get-TreeManifest([string]$root, [string[]]$files, [string]$tree) {
    $entries = @()
    foreach ($rel in $files) {
        $path = Join-Path $root $rel
        if (Test-Path -LiteralPath $path) {
            $entries += [pscustomobject]@{ Path = $rel; Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
    }
    $treeRoot = Join-Path $root $tree
    if (Test-Path -LiteralPath $treeRoot) {
        foreach ($f in Get-ChildItem -LiteralPath $treeRoot -Recurse -File -Force) {
            $rel = $f.FullName.Substring($root.Length).TrimStart('\')
            $entries += [pscustomobject]@{ Path = $rel; Hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash }
        }
    }
    return $entries | Sort-Object Path
}

function Compare-Manifest($expected, $actual) {
    $a = @{}; foreach ($e in $actual) { $a[$e.Path] = $e.Hash }
    $e2 = @{}; foreach ($e in $expected) { $e2[$e.Path] = $e.Hash }
    $diff = @()
    foreach ($k in $e2.Keys) { if (-not $a.ContainsKey($k)) { $diff += "missing $k" } elseif ($a[$k] -ne $e2[$k]) { $diff += "changed $k" } }
    foreach ($k in $a.Keys) { if (-not $e2.ContainsKey($k)) { $diff += "extra $k" } }
    return $diff
}

function Get-PatchHash {
    (Get-FileHash -LiteralPath (Join-Path $script:Game 'Data_Win32\patch.dat') -Algorithm SHA256).Hash
}

# Snapshots what a session may change in the real install, then puts the vanilla patch in place.
function Enter-ShotSession {
    $blocking = Get-Process | Where-Object { $_.ProcessName -in @('FarCry2', 'FCSE', 'FC2Editor', 'FC2Launcher', 'JackAll', 'jackall-cli', 'jackall-mi') }
    if ($blocking) { throw "Close these first: $(($blocking | ForEach-Object ProcessName | Sort-Object -Unique) -join ', ')" }
    if (Get-Process LogonUI -ErrorAction SilentlyContinue) { throw 'The Windows session is locked - WPF paints nothing while it is.' }
    if (Test-Path $script:SessionFile) { throw "A previous session was never closed. Run restore.ps1 first ($script:SessionFile)." }

    $snapshot = Join-Path $script:SessionRoot ('snapshots\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $snapshot | Out-Null
    foreach ($rel in $script:Guarded) {
        $src = Join-Path $script:Game $rel
        if (Test-Path -LiteralPath $src) {
            $dst = Join-Path $snapshot $rel
            New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
            Copy-Item -LiteralPath $src $dst
        }
    }
    $plugins = Join-Path $script:Game 'bin\plugins'
    if (Test-Path $plugins) { Copy-Item -LiteralPath $plugins (Join-Path $snapshot 'bin\plugins') -Recurse -Force }

    $manifest = @(Get-TreeManifest $script:Game $script:Guarded 'bin\plugins')
    $copied = @(Get-TreeManifest $snapshot $script:Guarded 'bin\plugins')
    $diff = @(Compare-Manifest $manifest $copied)
    if ($diff.Count) { throw "Snapshot does not match the install: $($diff -join '; ')" }

    $session = [pscustomobject]@{ Snapshot = $snapshot; Manifest = $manifest; Written = @() }
    $session | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $script:SessionFile
    Write-Host "Snapshot of $($manifest.Count) file(s) in $snapshot"
    Set-ShotBaseline
}

function Add-SessionPatch {
    $session = Get-Content -Raw $script:SessionFile | ConvertFrom-Json
    $session.Written = @($session.Written) + (Get-PatchHash) | Select-Object -Unique
    $session | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $script:SessionFile
}

# Vanilla patch, no plugins: what a fresh player has before installing anything.
function Set-ShotBaseline {
    if (-not (Test-Path $script:SessionFile)) { throw 'No session - call Enter-ShotSession first.' }
    $data = Join-Path $script:Game 'Data_Win32'
    Copy-Item -LiteralPath (Join-Path $data 'patch.dat.vanilla') (Join-Path $data 'patch.dat') -Force
    Copy-Item -LiteralPath (Join-Path $data 'patch.fat.vanilla') (Join-Path $data 'patch.fat') -Force
    $plugins = Join-Path $script:Game 'bin\plugins'
    if (Test-Path $plugins) { Get-ChildItem -LiteralPath $plugins -Force | Remove-Item -Recurse -Force }
    Add-SessionPatch
}

# Puts the snapshot back and proves it byte for byte. Refuses when someone else deployed meanwhile.
function Exit-ShotSession([switch]$Force) {
    Stop-ShotApp
    if (-not (Test-Path $script:SessionFile)) { return }
    $session = Get-Content -Raw $script:SessionFile | ConvertFrom-Json
    $live = Get-PatchHash
    if (-not $Force -and @($session.Written) -notcontains $live) {
        throw "patch.dat changed outside this session (another deploy?). Nothing restored. Check, then run restore.ps1 -Force."
    }

    $snapshot = $session.Snapshot
    foreach ($rel in $script:Guarded) {
        $src = Join-Path $snapshot $rel
        $dst = Join-Path $script:Game $rel
        if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src $dst -Force }
        elseif (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force }
    }
    $plugins = Join-Path $script:Game 'bin\plugins'
    if (Test-Path $plugins) { Get-ChildItem -LiteralPath $plugins -Force | Remove-Item -Recurse -Force }
    $saved = Join-Path $snapshot 'bin\plugins'
    if (Test-Path $saved) {
        New-Item -ItemType Directory -Force $plugins | Out-Null
        Get-ChildItem -LiteralPath $saved -Force | Copy-Item -Destination $plugins -Recurse -Force
    }

    $diff = @(Compare-Manifest @($session.Manifest) @(Get-TreeManifest $script:Game $script:Guarded 'bin\plugins'))
    if ($diff.Count) { throw "Restore did not verify: $($diff -join '; '). Snapshot kept in $snapshot." }
    Remove-Item $script:SessionFile
    Write-Host "Restored and verified $(@($session.Manifest).Count) file(s) in $script:Game"
}

# ---------------------------------------------------------------- the app

# Clean workspace and config for one tutorial: the game folder, these mod zips, the dark theme.
# -Workspace starts from another tutorial's saved result instead of an empty workspace.
function Reset-ShotState([string[]]$Mods = @(), [string]$Theme = 'dark', [string]$Workspace) {
    Stop-ShotApp
    Set-ShotBaseline
    foreach ($dir in 'workspace', 'data\prefabs') {
        $path = Join-Path $script:AppDir $dir
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    if ($Workspace) {
        $from = Join-Path $script:Bin "results\$Workspace\workspace"
        if (-not (Test-Path $from)) { throw "No saved result for '$Workspace' - run that tutorial first." }
        Copy-Item -LiteralPath $from (Join-Path $script:AppDir 'workspace') -Recurse
    }
    Set-ShotConfig -Mods $Mods -Theme $Theme
}

function Set-ShotConfig([string[]]$Mods = @(), [string]$Theme = 'dark', [string]$GamePath = $script:Game) {
    $lines = @('[game]', "path = $GamePath", '', '[mods]', '0 = workspace')
    $i = 1
    foreach ($m in $Mods) { $lines += "$i = $m"; $i++ }
    $lines += @('', '[ui]', "theme = $Theme")
    Set-Content -Encoding UTF8 (Join-Path $script:AppDir 'config.ini') $lines
}

function Start-ShotApp([switch]$NoWait, [int]$Width = 1280, [int]$Height = 800) {
    Stop-ShotApp
    $script:Process = Start-Process (Join-Path $script:AppDir 'JackAll.exe') -WorkingDirectory $script:AppDir -PassThru
    $script:Window = Wait-Ui { Find-ShotWindow 'JackAll*' } -Timeout 60 -What 'the JackAll window'
    Set-ShotWindowSize $Width $Height
    if (-not $NoWait) { Wait-ShotReady }
    return $script:Window
}

# Re-attaches to a shots app started by an earlier script.
function Connect-ShotApp {
    $script:Process = Get-Process JackAll -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($script:AppDir, 'OrdinalIgnoreCase') } | Select-Object -First 1
    if (-not $script:Process) { throw 'The shots app is not running.' }
    $script:Window = Find-ShotWindow 'JackAll*'
    return $script:Window
}

function Set-ShotWindowSize([int]$Width = 1280, [int]$Height = 800) {
    [ShotNative]::Place([IntPtr]$script:Window.Current.NativeWindowHandle, 24, 24, $Width, $Height)
    Start-Sleep -Milliseconds 300
}

function Stop-ShotApp {
    Get-Process JackAll -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($script:AppDir, 'OrdinalIgnoreCase') } | Stop-Process -Force
    $script:Process = $null
    $script:Window = $null
}

# A top-level window of the shots app whose title matches, e.g. a dialog it opened.
function Find-ShotWindow([string]$Title) {
    if (-not $script:Process) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $script:Process.Id)
    $windows = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) {
        if ($w.Current.Name -like $Title) { return $w }
        foreach ($child in $w.FindAll($TS::Descendants, $windows)) {
            if ($child.Current.Name -like $Title) { return $child }
        }
    }
    return $null
}

# The status line at the bottom of the window.
function Get-ShotStatusElement {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    return $script:Window.FindFirst($TS::Children, $cond)
}

function Get-ShotStatus {
    $text = Get-ShotStatusElement
    if ($text) { return $text.Current.Name }
    return ''
}

function Test-ShotBusy {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ProgressBar)
    $bar = $script:Window.FindFirst($TS::Children, $cond)
    return [bool]($bar -and -not $bar.Current.IsOffscreen)
}

# Loaded, every .fcb indexed, nothing running.
function Wait-ShotReady([int]$Timeout = 900) {
    Wait-Ui { (Get-ShotStatus) -match '^[\d,]+ files across \d+ archives\s+.\s+[\d,]+ with unknown names$' -and -not (Test-ShotBusy) } -Timeout $Timeout -What 'JackAll to finish loading' | Out-Null
}

function Wait-ShotStatus([string]$Like, [int]$Timeout = 300) {
    Wait-Ui { (Get-ShotStatus) -like $Like -and -not (Test-ShotBusy) } -Timeout $Timeout -What "status '$Like'" | Out-Null
    return Get-ShotStatus
}

# Zips a layer folder (mods\ and/or plugins\ at its root) into bin\inputs, as a player downloads it.
function New-ShotZip([string]$Name, [string]$LayerDir) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $dir = Join-Path $script:Bin 'inputs'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir $Name
    if (Test-Path $zip) { Remove-Item $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($LayerDir, $zip)
    return $zip
}

function Save-ShotResult([string]$Slug) {
    $dst = Join-Path $script:Bin "results\$Slug\workspace"
    if (Test-Path $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    New-Item -ItemType Directory -Force $dst | Out-Null
    $ws = Join-Path $script:AppDir 'workspace'
    if (Test-Path $ws) { Get-ChildItem -LiteralPath $ws -Force | Copy-Item -Destination $dst -Recurse -Force }
}

# ---------------------------------------------------------------- UI Automation

function Wait-Ui([scriptblock]$Check, [int]$Timeout = 15, [string]$What = 'condition') {
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ($true) {
        $result = $null
        try { $result = & $Check } catch { $result = $null }
        if ($result) { return $result }
        if ((Get-Date) -gt $deadline) { throw "Timed out after $Timeout s waiting for $What" }
        Start-Sleep -Milliseconds 250
    }
}

# Finds one element (or -All) below Scope by AutomationId, exact name, name pattern and/or control type.
function Find-Ui {
    param([string]$Id, [string]$Name, [string]$Like, [string]$Type, $Scope, [int]$Timeout = 15, [switch]$All, [switch]$Optional, [switch]$Children)
    if (-not $Scope) { $Scope = $script:Window }
    $conds = @()
    if ($Id) { $conds += New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id) }
    if ($Name) { $conds += New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name) }
    if ($Type) { $conds += New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::$Type) }
    $cond = switch ($conds.Count) {
        0 { [System.Windows.Automation.Condition]::TrueCondition }
        1 { $conds[0] }
        default { [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]]$conds) }
    }
    $scopeKind = if ($Children) { $TS::Children } else { $TS::Descendants }
    $find = {
        $found = @($Scope.FindAll($scopeKind, $cond) | Where-Object { -not $Like -or $_.Current.Name -like $Like })
        if ($All) { if ($found.Count) { , $found } } elseif ($found.Count) { $found[0] }
    }
    if ($Optional) { $result = & $find; return $result }
    return Wait-Ui $find -Timeout $Timeout -What "element Id=$Id Name=$Name Like=$Like Type=$Type"
}

function Invoke-Ui($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $p.Toggle(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); return }
    throw "Nothing to invoke on '$($Element.Current.Name)'"
}


function Set-UiValue($Element, [string]$Value) {
    $p = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $p.SetValue($Value)
}

function Get-UiValue($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p)) { return $p.Current.Value }
    return $Element.Current.Name
}

function Set-UiToggle($Element, [bool]$On) {
    $p = $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($p.Current.ToggleState -eq 'On') -ne $On) { $p.Toggle() }
}

function Select-Ui($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$p)) { $p.ScrollIntoView() }
    $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Expand-Ui($Element) {
    $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
}

function Select-UiTab([string]$Header) {
    $tabs = Find-Ui -Id MainTabs
    Select-Ui (Find-Ui -Scope $tabs -Type TabItem -Like $Header -Children)
    Start-Sleep -Milliseconds 400
}

# Picks an entry in a ComboBox by its displayed name.
function Select-UiCombo($Combo, [string]$Like) {
    Expand-Ui $Combo
    $item = Find-Ui -Scope $Combo -Type ListItem -Like $Like
    Select-Ui $item
    $p = $null
    if ($Combo.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Collapse() }
}

# The text a templated item shows; its own automation name is often just its data class.
function Get-UiText($Element) {
    $texts = @($Element.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text))) | ForEach-Object { $_.Current.Name })
    return ($texts -join ' ').Trim()
}

# Items of a type below Scope whose shown text matches the pattern.
function Find-UiByText {
    param([Parameter(Mandatory)][string]$Like, [string]$Type = 'TreeItem', $Scope, [int]$Timeout = 15, [switch]$All)
    if (-not $Scope) { $Scope = $script:Window }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::$Type)
    $find = {
        $found = @($Scope.FindAll($TS::Descendants, $cond) | Where-Object { (Get-UiText $_) -like $Like -or $_.Current.Name -like $Like })
        if ($All) { if ($found.Count) { , $found } } elseif ($found.Count) { $found[0] }
    }
    return Wait-Ui $find -Timeout $Timeout -What "$Type showing '$Like'"
}

# Scrolls the element, or its nearest ancestor that can be scrolled to, into view.
function Show-Ui($Element) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $e = $Element
    while ($e) {
        $p = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$p)) {
            $p.ScrollIntoView()
            Start-Sleep -Milliseconds 250
            return
        }
        if ($e -ne $Element -and $e.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$p) -and $p.Current.VerticallyScrollable) {
            $view = $e.Current.BoundingRectangle
            for ($i = 0; $i -lt 200; $i++) {
                $r = $Element.Current.BoundingRectangle
                if ($r.Bottom -gt $view.Bottom - 8) { $p.Scroll('NoAmount', 'SmallIncrement') }
                elseif ($r.Top -lt $view.Top + 8) { $p.Scroll('NoAmount', 'SmallDecrement') }
                else { break }
            }
            Start-Sleep -Milliseconds 250
            return
        }
        $e = $walker.GetParent($e)
    }
}

# Expands a value-editor section (an Expander) by the start of its header, e.g. 'Ammo*'.
function Open-UiSection([string]$Like, $Scope) {
    $group = Find-UiByText -Scope $Scope -Type Group -Like $Like
    Expand-Ui $group
    Start-Sleep -Milliseconds 400
    return $group
}

# The editor control of a value-editor field row, by the field's name.
function Find-UiField([string]$Label, $Scope) {
    $row = Wait-Ui { @(Find-Ui -Scope $Scope -Type DataItem -All -Optional | Where-Object { (Find-Ui -Scope $_ -Type Text -Children -All -Optional | ForEach-Object { $_.Current.Name }) -contains $Label })[0] } -What "field '$Label'"
    Show-Ui $row
    # A changed field also carries Restore/Revert buttons; the editor is the input control.
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($row)
    $fallback = $null
    while ($child) {
        $type = $child.Current.ControlType
        if ($type -in @($CT::Edit, $CT::ComboBox, $CT::CheckBox)) { return $child }
        if (-not $fallback -and $type -notin @($CT::Text, $CT::Button)) { $fallback = $child }
        $child = $walker.GetNextSibling($child)
    }
    if ($fallback) { return $fallback }
    return $row
}

# The Restore (or Revert) button a changed field shows, by the field's name.
function Find-UiFieldButton([string]$Label, [string]$Button, $Scope) {
    $editor = Find-UiField $Label $Scope
    $row = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($editor)
    return Find-Ui -Scope $row -Type Button -Name $Button -Children
}

# Brings a row of a virtualized grid or tree into existence by its name.
function Get-UiItem($Container, [string]$Name) {
    $p = $null
    if ($Container.TryGetCurrentPattern([System.Windows.Automation.ItemContainerPattern]::Pattern, [ref]$p)) {
        $item = $p.FindItemByProperty($null, $AE::NameProperty, $Name)
        if ($item) {
            $v = $null
            if ($item.TryGetCurrentPattern([System.Windows.Automation.VirtualizedItemPattern]::Pattern, [ref]$v)) { $v.Realize() }
            return $item
        }
    }
    return Find-Ui -Scope $Container -Name $Name
}

# Fills a Windows file or folder dialog and confirms it.
function Complete-FileDialog([string]$Title, [string]$Path) {
    $dialog = Wait-Ui { Find-ShotWindow $Title } -Timeout 20 -What "dialog '$Title'"
    $edit = Wait-Ui { @(Find-Win32Control $dialog 'Edit' | Where-Object { $_.Current.AutomationId -in '1148', '1152' })[0] } -What 'the file name box'
    [ShotNative]::SetText([IntPtr]$edit.Current.NativeWindowHandle, $Path)
    Start-Sleep -Milliseconds 200
    Push-ShotButton $dialog '1'
}

# Win32 child controls of a dialog by window class, e.g. 'Edit' or 'Button'.
function Find-Win32Control($Dialog, [string]$Class) {
    $Dialog.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, $Class)))
}

# Presses a Win32 dialog button by control id ('1' OK/Open, '2' Cancel, '6' Yes, '7' No) or label.
function Push-ShotButton($Dialog, [string]$IdOrLabel) {
    $button = Wait-Ui { @(Find-Win32Control $Dialog 'Button' | Where-Object { $_.Current.AutomationId -eq $IdOrLabel -or $_.Current.Name -like $IdOrLabel })[0] } -What "button '$IdOrLabel'"
    [ShotNative]::PressButton([IntPtr]$button.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 300
}

# Presses a button of a message box (or any dialog) by its label, after waiting for it to open.
function Close-ShotDialog([string]$Title, [string]$Button = 'OK') {
    $dialog = Wait-Ui { Find-ShotWindow $Title } -Timeout 30 -What "dialog '$Title'"
    Push-ShotButton $dialog $Button
}

function Get-DialogText($Dialog) {
    (@(Find-Win32Control $Dialog 'Static') + @($Dialog.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)))) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique) -join "`n"
}

# ---------------------------------------------------------------- real input

$script:VirtualKeys = @{ Ctrl = 0x11; Shift = 0x10; Alt = 0x12; Left = 0x25; Right = 0x27; Up = 0x26; Down = 0x28; Del = 0x2E; Esc = 0x1B; Enter = 0x0D; Tab = 0x09; Space = 0x20; F = 0x46; T = 0x54; R = 0x52; C = 0x43; V = 0x56; X = 0x58; Z = 0x5A; Y = 0x59; G = 0x47 }

# Sends a key chord such as 'Ctrl+Z' to the foreground window.
function Send-ShotKeys([string]$Chord) {
    [ShotNative]::Front([IntPtr]$script:Window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 150
    $vks = [uint16[]]@($Chord.Split('+') | ForEach-Object { $script:VirtualKeys[$_] })
    [ShotNative]::Keys($vks)
    Start-Sleep -Milliseconds 200
}

function Click-Ui($Element, [switch]$Right, [double]$X = 0.5, [double]$Y = 0.5) {
    [ShotNative]::Front([IntPtr]$script:Window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 150
    $r = $Element.Current.BoundingRectangle
    [ShotNative]::Click([int]($r.X + $r.Width * $X), [int]($r.Y + $r.Height * $Y), [bool]$Right)
    Start-Sleep -Milliseconds 250
}

# ---------------------------------------------------------------- capture

# Captures a window, outlines and numbers the callouts in order, crops to the region and writes
# docs/static/img/jackall/<Slug>/<Name>.png (or .webp with -Photo).
function Save-Shot {
    param([Parameter(Mandatory)][string]$Slug, [Parameter(Mandatory)][string]$Name,
          [object[]]$Callouts = @(), [object[]]$Region = @(), $Window, [switch]$Screen, [switch]$Photo,
          [int]$Pad = 16, [switch]$NoPark)
    if (-not $Window) { $Window = $script:Window }
    $hwnd = [IntPtr]$Window.Current.NativeWindowHandle
    if ($Screen) { [ShotNative]::Front($hwnd); Start-Sleep -Milliseconds 300 }
    if (-not $NoPark) { [ShotNative]::Park(2500, 1400) }
    Start-Sleep -Milliseconds 300

    $frame = [ShotNative]::Bounds($hwnd)
    $bmp = [ShotNative]::Capture($hwnd, [bool]$Screen)
    if ([ShotNative]::IsBlank($bmp)) { throw "Capture of '$Name' is blank - locked session or an unpainted window." }

    $rel = { param($e) $r = $e.Current.BoundingRectangle; New-Object System.Drawing.Rectangle ([int]$r.X - $frame.X), ([int]$r.Y - $frame.Y), ([int]$r.Width), ([int]$r.Height) }
    $rects = [System.Drawing.Rectangle[]]@($Callouts | ForEach-Object { & $rel $_ })
    $crop = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    if ($Region.Count) {
        $crop = [System.Drawing.Rectangle]::Empty
        foreach ($r in @($Region | ForEach-Object { & $rel $_ }) + $rects) {
            $crop = if ($crop.IsEmpty) { $r } else { [System.Drawing.Rectangle]::Union($crop, $r) }
        }
        $crop = New-Object System.Drawing.Rectangle ($crop.X - $Pad), ($crop.Y - $Pad), ($crop.Width + 2 * $Pad), ($crop.Height + 2 * $Pad)
    }
    $final = [ShotNative]::Annotate($bmp, $rects, $crop)
    $bmp.Dispose()

    $rawDir = Join-Path $script:Bin "raw\$Slug"
    New-Item -ItemType Directory -Force $rawDir | Out-Null
    $raw = Join-Path $rawDir "$Name.png"
    $previous = if (Test-Path $raw) { (Get-FileHash $raw).Hash } else { '' }
    $final.Save($raw, [System.Drawing.Imaging.ImageFormat]::Png)
    $final.Dispose()

    $outDir = Join-Path $script:ImageRoot $Slug
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $ext = if ($Photo) { 'webp' } else { 'png' }
    $out = Join-Path $outDir "$Name.$ext"
    if ((Get-FileHash $raw).Hash -ne $previous -or -not (Test-Path $out)) {
        if ($Photo) {
            & $script:Ffmpeg -y -v error -i $raw -vf "scale='min(1200,iw)':-2:flags=lanczos" -c:v libwebp -quality 85 $out
        } else {
            & $script:Ffmpeg -y -v error -i $raw -vf 'split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=none' $out
        }
        if ($LASTEXITCODE) { throw "ffmpeg failed on $raw" }
    }
    $names = @($Callouts | ForEach-Object { if ($_.Current.AutomationId) { $_.Current.AutomationId } else { $_.Current.Name } })
    Add-ShotManifest $Slug ([pscustomobject]@{ Image = "$Name.$ext"; Bytes = (Get-Item $out).Length; Callouts = $names })
    Write-Host ("  {0}/{1}.{2}  {3:N0} KB" -f $Slug, $Name, $ext, ((Get-Item $out).Length / 1KB))
}

function Add-ShotManifest([string]$Slug, $Entry) {
    $dir = Join-Path $script:Bin 'manifest'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $path = Join-Path $dir "$Slug.json"
    $entries = @()
    if (Test-Path $path) {
        $parsed = Get-Content -Raw $path | ConvertFrom-Json
        $entries = @($parsed | Where-Object { $_.Image -ne $Entry.Image })
    }
    ConvertTo-Json -Depth 3 -InputObject @($entries + $Entry) | Set-Content -Encoding UTF8 $path
}

Export-ModuleMember -Function * -Variable AE, CT, TS
