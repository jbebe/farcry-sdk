<#
.SYNOPSIS
    Reads one section out of a project's Keep a Changelog file.
.DESCRIPTION
    With no -Section, that is the newest released version - the topmost '## [x]' heading that is
    not Unreleased, which is the version a release publishes.
.EXAMPLE
    ./changelog.ps1 -Path tools/JackAll/CHANGELOG.md -Render version
.EXAMPLE
    ./changelog.ps1 -Path tools/JackAll/CHANGELOG.md -Render nexus
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,

    # Version to read, or 'Unreleased'. Defaults to the newest released version.
    [string]$Section,

    # 'version' is the section's own version, 'markdown' its body verbatim for a GitHub release,
    # and 'nexus' its body as the plain text the Nexus Mods changelog box takes - that one renders
    # neither Markdown nor BBCode.
    [ValidateSet('version', 'markdown', 'nexus')]
    [string]$Render = 'markdown'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# [System.IO.File] resolves a relative path against the process directory, which is not where the
# shell thinks it is after a cd.
$Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)

$lines = @([System.IO.File]::ReadAllText($Path) -split "`r?`n")

$headings = [ordered]@{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\[([^\]]+)\]') {
        $headings[$matches[1]] = $i
    }
}

if (-not $Section) {
    $Section = $headings.Keys | Where-Object { $_ -ne 'Unreleased' } | Select-Object -First 1
    if (-not $Section) {
        throw "$Path documents no released version yet."
    }
}
if (-not $headings.Contains($Section)) {
    throw "$Path has no '## [$Section]' section."
}

if ($Render -eq 'version') {
    return $Section
}

# The section's body: everything up to the next '## ' heading, stripped of the blank lines that pad
# it away from its neighbours.
$start = $headings[$Section] + 1
$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s') { $end = $i; break }
}
$first = $start
while ($first -lt $end -and -not $lines[$first].Trim()) { $first++ }
$last = $end - 1
while ($last -ge $first -and -not $lines[$last].Trim()) { $last-- }
if ($last -lt $first) {
    throw "$Path has nothing under '## [$Section]'."
}
$body = @($lines[$first..$last])

if ($Render -eq 'markdown') {
    return ($body -join "`n")
}

function ConvertTo-PlainText([string]$text) {
    $text = $text -replace '\[([^\]]+)\]\(([^)]+)\)', '$1 ($2)'
    $text = $text -replace '`', ''
    $text = $text -replace '\*\*([^*]+)\*\*', '$1'
    $text = $text -replace '(?<!\*)\*([^*]+)\*(?!\*)', '$1'
    return $text
}

# Markdown hard-wraps a bullet across several lines; Nexus shows every line break it is given, so
# each bullet has to come back out as the one line it reads as.
$out = [System.Collections.Generic.List[string]]::new()
$continuing = $false
$blankPending = $false
foreach ($line in $body) {
    if (-not $line.Trim()) {
        $continuing = $false
        $blankPending = $true
        continue
    }
    if ($blankPending -and $out.Count) { $out.Add('') }
    $blankPending = $false

    if ($line -match '^#{3,}\s+(.*)') {
        if ($out.Count -and $out[$out.Count - 1] -ne '') { $out.Add('') }
        $out.Add($matches[1] + ':')
        $continuing = $false
    }
    elseif ($line -match '^(\s*)[-*]\s+(.*)') {
        $out.Add($matches[1] + '- ' + $matches[2])
        $continuing = $true
    }
    elseif ($continuing) {
        $out[$out.Count - 1] = $out[$out.Count - 1] + ' ' + $line.Trim()
    }
    else {
        $out.Add($line.Trim())
        $continuing = $true
    }
}

# Markup is stripped only once the wrapped lines are joined back up, so a link or a bold span the
# source broke across two lines still matches.
return (($out | ForEach-Object { ConvertTo-PlainText $_ }) -join "`n")
