# What the build scripts share, dot-sourced: the repository, the extracted game, JackAll's CLI, and Exec.
$Repo = Resolve-Path "$PSScriptRoot\..\.."
# The gitignored tmp\ and texconv live in the main working tree, also when this runs from a worktree.
$MainTree = Split-Path -Parent (git -C $Repo rev-parse --path-format=absolute --git-common-dir)
$Tmp = Join-Path $MainTree "tmp"
$Texconv = "$MainTree\tools\third-party\FCBConverter\texconv.exe"
$Cli = "$Repo\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"

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
