<#
.SYNOPSIS
    Sets each project's Nexus Mods page summary and description from its assets\nexus-summary.txt and
    assets\nexus-description.bbcode, through the v3 editMod endpoint.

.DESCRIPTION
    A project's page is the NEXUS_MOD_ID of the release workflow that prepares its directory, so the
    ID lives in one place. A project with no description file, or no mod ID, is skipped with a warning,
    as is every one when NEXUSMODS_API_KEY is unset.

.PARAMETER Project
    Project directories, such as mods/UFCP. Empty means every project with a description file.

.PARAMETER Check
    Compares each page's text with the live one, ignoring line breaks and spacing, and says which
    would change, sending nothing.

.EXAMPLE
    ./scripts/nexus-page.ps1 -Project mods/UFCP -Check
#>
param(
    [string[]]$Project = @(),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$farCry2 = [int64]1471 -shl 32
$summaryLimit = 350

function Get-PageProjects {
    Get-ChildItem $root -Recurse -Filter 'nexus-description.bbcode' |
        Where-Object { $_.Directory.Name -eq 'assets' -and $_.FullName -notmatch '[\\/](node_modules|tmp)[\\/]' } |
        ForEach-Object { $_.Directory.Parent.FullName.Substring($root.Length + 1).Replace('\', '/') }
}

function Get-ModId([string]$directory) {
    foreach ($workflow in Get-ChildItem (Join-Path $root '.github/workflows') -Filter '*-release.yml') {
        $text = Get-Content $workflow.FullName -Raw
        if ($text -match "directory:\s*$([regex]::Escape($directory))\s" -and $text -match "NEXUS_MOD_ID:\s*'(\d*)'") {
            return $Matches[1]
        }
    }
    return ''
}

function Read-Text([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    return ([IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) -replace "`r`n", "`n").Trim()
}

# The site stores line breaks its own way, so a page is compared by its text alone.
function Get-Text([string]$text) {
    return ([Net.WebUtility]::HtmlDecode(($text -replace '<br\s*/?>', ''))) -replace '\s+', ''
}

$key = $env:NEXUSMODS_API_KEY
if (-not $key) {
    Write-Host '::warning::NEXUSMODS_API_KEY is not set - no mod page is updated.'
    exit 0
}
$headers = @{ apikey = $key }

$projects = @($Project | Where-Object { $_ })
if ($projects.Count -eq 0) { $projects = @(Get-PageProjects) }

$failed = $false
foreach ($directory in $projects) {
    $description = Read-Text (Join-Path $root "$directory/assets/nexus-description.bbcode")
    $summary = Read-Text (Join-Path $root "$directory/assets/nexus-summary.txt")
    $modId = Get-ModId $directory
    if (-not $description -or -not $modId) {
        $missing = if (-not $description) { 'nexus-description.bbcode' } else { 'NEXUS_MOD_ID' }
        Write-Host "::warning::$directory has no $missing - its page is not updated."
        continue
    }

    $body = [ordered]@{ description = $description }
    if (-not $summary) {
        Write-Host "::warning::$directory has no nexus-summary.txt - only its description is set."
    } else {
        $summary = ($summary -replace '\s+', ' ').Trim()
        if ($summary.Length -gt $summaryLimit) {
            Write-Host "::error::$directory's summary is $($summary.Length) characters; Nexus takes at most $summaryLimit."
            $failed = $true
            continue
        }
        $body.summary = $summary
    }

    if ($Check) {
        $gameScoped = [int64]$modId - $farCry2
        if ($gameScoped -lt 0 -or $gameScoped -ge ([int64]1 -shl 32)) {
            Write-Host "$directory : not a Far Cry 2 page, so not compared"
            continue
        }
        $live = Invoke-RestMethod "https://api.nexusmods.com/v1/games/farcry2/mods/$gameScoped.json" -Headers $headers
        $changes = @()
        if ((Get-Text $live.description) -ne (Get-Text $description)) { $changes += 'description' }
        if ($summary -and (Get-Text $live.summary) -ne (Get-Text $summary)) { $changes += 'summary' }
        $verdict = if ($changes) { 'would change the ' + ($changes -join ' and ') } else { 'matches the live page' }
        Write-Host "$directory : $verdict"
        continue
    }

    $json = $body | ConvertTo-Json -Compress
    try {
        Invoke-RestMethod "https://api.nexusmods.com/v3/mods/$modId" -Method Patch -Headers $headers `
            -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
        Write-Host "$directory : page updated ($(($body.Keys) -join ' and '))"
    } catch {
        Write-Host "::error::$directory : the page update failed - $($_.Exception.Message) $($_.ErrorDetails.Message)"
        $failed = $true
    }
}

if ($failed) { exit 1 }
