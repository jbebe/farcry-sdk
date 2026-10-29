# What the build scripts share, dot-sourced: the repository, JackAll's CLI, and Exec.
$Repo = Resolve-Path "$PSScriptRoot\..\.."
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
