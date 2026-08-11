<#
.SYNOPSIS
    Checks the properties of a built FCSE.exe that fail *silently* - the exe still builds, still runs
    on a dev machine, and only breaks on a player's install.

.DESCRIPTION
    Two things about this build can go wrong without producing a warning, let alone an error:

      - Static CRT (/MT). If the CMP0091 setting in CMakeLists.txt stops applying, FCSE.exe imports
        MSVCP140/VCRUNTIME140/the UCRT apisets, and a player without the VS 2015-2022 x86
        redistributable gets a missing-DLL box before any of our code runs - no fcse.log, nothing to
        diagnose from, and no way to tell it apart from FCSE being broken.

      - The embedded resources. A .rc added to a project without enable_language(RC) is skipped
        without a word, producing an FCSE.exe whose settings page has no layout and whose scripts all
        fail to require 'fcse'.

    Both are read straight out of the built image rather than through dumpbin, which is not on PATH
    for a caller that has not been through vcvarsall (build.ps1 sets up the developer environment
    inside its own `cmd /c` and nothing inherits it). The CRT names and the RCDATA payloads are plain
    bytes in the file: a static-CRT build contains none of the former (checked against a real build,
    not assumed), and matching the payloads byte-for-byte also catches an embedded copy that went
    stale against the layout the build just encoded.

    Run by both FCSE workflows in .github/workflows - on every push and pull request, and again
    before a release is packaged - which is the reason it is a script here and not a step in either.
    See README.md's "Verification" section for the checks that still need a real install.

.PARAMETER Config
    "release" or "debug" - selects which build tree under out\build\ to check. Defaults to "release".

.EXAMPLE
    .\verify_build.ps1
    .\verify_build.ps1 -Config debug
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$BuildDir = Join-Path $ProjectRoot "out\build\x86-$Config"
$ExePath = Join-Path $BuildDir "FCSE.exe"

if (-not (Test-Path $ExePath)) {
    throw "$ExePath does not exist - build it first with .\build.ps1 -Config $Config."
}

# 28591 = Latin-1: one byte <-> one char, so a byte search is a plain string search.
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$exe = $latin1.GetString([System.IO.File]::ReadAllBytes($ExePath))

# Substring matches, so the debug CRT is covered by the same names: a /MD build of the Debug
# configuration imports MSVCP140D.dll and VCRUNTIME140D.dll.
$crt = @("MSVCP140", "VCRUNTIME140", "api-ms-win-crt-", "ucrtbase") |
       Where-Object { $exe.Contains($_) }
if ($crt) {
    throw "$ExePath links the dynamic CRT (found: $($crt -join ', ')) - it must build /MT. See the CMP0091 note in CMakeLists.txt."
}

# The layouts are read from the build tree because that is where they exist: they are built from
# assets\*.mgb.xml by JackAll's mgb encoder during the CMake build, and no binary .mgb is committed.
# The Lua runtime is a source file, embedded the same way and failing the same way - a missing RCDATA
# resource still builds and still launches, and only shows up as every script in the install failing.
foreach ($asset in (Join-Path $BuildDir "assets\fcse.mgb"),
                   (Join-Path $BuildDir "assets\fcse_widescreen.mgb"),
                   (Join-Path $ProjectRoot "src\lua\runtime\fcse.lua")) {
    $payload = $latin1.GetString([System.IO.File]::ReadAllBytes($asset))
    if ($exe.IndexOf($payload, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$asset is not embedded in $ExePath, or the embedded copy is stale - check that enable_language(RC) ran and that the generated fcse.rc reached the link."
    }
}

Write-Host "FCSE.exe ($Config): static CRT, both .mgb layouts and the Lua runtime embedded." -ForegroundColor Green
