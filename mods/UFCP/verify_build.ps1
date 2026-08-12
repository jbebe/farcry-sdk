<#
.SYNOPSIS
    Checks the properties of a built UFCP.dll that fail *silently* - the DLL still builds, still
    loads on a dev machine, and only breaks on a player's install.

.DESCRIPTION
    Three things about this build can go wrong without producing a warning, let alone an error:

      - Architecture. A 64-bit UFCP.dll builds perfectly well and can never load into Far Cry 2,
        which is a 32-bit process. FCSE reports it as a plugin that failed to load, with nothing to
        say why beyond the OS error.

      - Static CRT (/MT). If the CMP0091 setting in CMakeLists.txt stops applying, UFCP.dll imports
        MSVCP140/VCRUNTIME140/the UCRT apisets, and a player without the VS 2015-2022 x86
        redistributable has a plugin that silently never loads - while FCSE itself, which is /MT,
        starts fine and gives no hint that the missing runtime is the reason.

      - The FCSE_Load export. It is the entire contract: FCSE looks up that one name and skips any
        DLL without it. Losing __declspec(dllexport) leaves a DLL that is found, loaded, and
        ignored.

    All three are read straight out of the built image rather than through dumpbin, which is not on
    PATH for a caller that has not been through vcvarsall (build.ps1 sets up the developer
    environment inside its own `cmd /c` and nothing inherits it).

    Run by both UFCP workflows in .github/workflows - on every push and pull request, and again
    before a release is packaged - which is the reason it is a script here and not a step in either.
    See README.md's "Verification" section for the checks that still need a real install, and
    verify_patterns.py for the byte patterns, which need a copy of the game and therefore cannot run
    in CI.

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
$DllPath = Join-Path $ProjectRoot "out\build\x86-$Config\UFCP.dll"

if (-not (Test-Path $DllPath)) {
    throw "$DllPath does not exist - build it first with .\build.ps1 -Config $Config."
}

$bytes = [System.IO.File]::ReadAllBytes($DllPath)

# IMAGE_DOS_HEADER.e_lfanew is at 0x3C and points at the PE signature; IMAGE_FILE_HEADER.Machine is
# the first field after it. 0x014C is x86, 0x8664 is x64.
$peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
$machine = [System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x014C) {
    throw "$DllPath is machine 0x{0:X4}, not x86 (0x014C) - Far Cry 2 is a 32-bit process and cannot load it. Use the x86-$Config preset." -f $machine
}

# 28591 = Latin-1: one byte <-> one char, so a byte search is a plain string search.
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$image = $latin1.GetString($bytes)

# Substring matches, so the debug CRT is covered by the same names: a /MD build of the Debug
# configuration imports MSVCP140D.dll and VCRUNTIME140D.dll.
$crt = @("MSVCP140", "VCRUNTIME140", "api-ms-win-crt-", "ucrtbase") |
       Where-Object { $image.Contains($_) }
if ($crt) {
    throw "$DllPath links the dynamic CRT (found: $($crt -join ', ')) - it must build /MT. See the CMP0091 note in CMakeLists.txt."
}

# The export name lives in the image as a plain string, and appears nowhere else in this DLL - there
# is no string literal by that name in the sources.
if (-not $image.Contains("FCSE_Load")) {
    throw "$DllPath does not export FCSE_Load - FCSE loads a plugin by that one name and skips any DLL without it. Check the extern `"C`" __declspec(dllexport) on it in src\main.cpp."
}

Write-Host "UFCP.dll ($Config): x86, static CRT, exports FCSE_Load." -ForegroundColor Green
