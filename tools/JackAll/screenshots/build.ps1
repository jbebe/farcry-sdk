# Builds the app and CLI the screenshots are taken with into bin\, apart from any JackAll you run yourself.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\src'
$bin = Join-Path $PSScriptRoot 'bin'

dotnet build (Join-Path $src 'JackAll.App\JackAll.App.csproj') -c Release -o (Join-Path $bin 'app') -v q -nologo
if ($LASTEXITCODE) { throw 'App build failed' }
dotnet build (Join-Path $src 'JackAll.Cli\JackAll.Cli.csproj') -c Release -o (Join-Path $bin 'cli') -v q -nologo
if ($LASTEXITCODE) { throw 'CLI build failed' }
