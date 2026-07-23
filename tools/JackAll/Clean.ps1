<#
.SYNOPSIS
    Removes the obj/ and bin/ output folders from every C# project under this folder.
#>

$ErrorActionPreference = 'Stop'

$projectDirs = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter *.csproj |
    ForEach-Object { $_.Directory.FullName } |
    Select-Object -Unique

$removed = 0
foreach ($projectDir in $projectDirs) {
    foreach ($name in 'obj', 'bin') {
        $path = Join-Path $projectDir $name
        if (Test-Path $path) {
            Write-Output "Removing $path"
            Remove-Item -Path $path -Recurse -Force
            $removed++
        }
    }
}

if ($removed -eq 0) {
    Write-Output 'Skipped - no obj/bin folders found.'
} else {
    Write-Output "Removed $removed folder(s)."
}
