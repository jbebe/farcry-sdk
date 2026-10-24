# Builds layer\: a new world named testrange from the installed game's tmpla, with content\'s
# fragments packed into it.
param(
    [string]$Game = 'C:\Games\Far Cry 2',
    [string]$Cli = "$PSScriptRoot\..\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
)
$ErrorActionPreference = 'Stop'

$source = Join-Path ([IO.Path]::GetTempPath()) 'testrange-source'
foreach ($dir in $source, "$PSScriptRoot\layer") { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }

& $Cli world new testrange --game $Game --out $source
if ($LASTEXITCODE -ne 0) { throw 'world new failed' }
Copy-Item -Recurse -Force "$PSScriptRoot\content\mods" $source
& "$PSScriptRoot\plugin\build.ps1"
New-Item -ItemType Directory -Force "$source\plugins\testrange" | Out-Null
Copy-Item "$PSScriptRoot\plugin\out\build\x86-release\TestRange.dll" "$source\plugins\testrange\"
& $Cli world pack $source --game $Game --out "$PSScriptRoot\layer"
if ($LASTEXITCODE -ne 0) { throw 'world pack failed' }
Remove-Item -Recurse -Force $source
