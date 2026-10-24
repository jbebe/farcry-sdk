# Builds ..\layer\: the test range, a new world named testrange from the installed game's tmpla, with
# content\'s fragments packed into it.
param(
    [string]$Game = 'C:\Games\Far Cry 2',
    [string]$Cli = "$PSScriptRoot\..\..\..\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
)
$ErrorActionPreference = 'Stop'

# The CLI reports progress on stderr, which Windows PowerShell would otherwise stop the script on.
function Invoke-Cli {
    $ErrorActionPreference = 'Continue'
    & $Cli @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "jackall-cli $($args[0]) $($args[1]) failed" }
}

$source = Join-Path ([IO.Path]::GetTempPath()) 'testrange-source'
$layer = "$PSScriptRoot\..\layer"
foreach ($dir in $source, $layer) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }

Invoke-Cli world new testrange --game $Game --out $source
Copy-Item -Recurse -Force "$PSScriptRoot\content\mods" $source
Invoke-Cli world pack $source --game $Game --out $layer
Remove-Item -Recurse -Force $source
