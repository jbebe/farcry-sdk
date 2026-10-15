<#
.SYNOPSIS
    Checks the properties of a built LegacyPlugin.dll that fail silently on a player's install.

.DESCRIPTION
    The same three checks as mods/UFCP/verify_build.ps1, which explains each: x86, static CRT, and
    the FCSE_Load export. Run by .github/workflows/legacy-plugin-ci.yml.

.PARAMETER Config
    "release" or "debug" - selects which build tree under out\build\ to check. Defaults to "release".
#>
param(
    [ValidateSet("release", "debug")]
    [string]$Config = "release"
)

$ErrorActionPreference = "Stop"

$DllPath = Join-Path $PSScriptRoot "out\build\x86-$Config\LegacyPlugin.dll"

if (-not (Test-Path $DllPath)) {
    throw "$DllPath does not exist - build it first with .\build.ps1 -Config $Config."
}

$bytes = [System.IO.File]::ReadAllBytes($DllPath)

# IMAGE_FILE_HEADER.Machine, right after the PE signature e_lfanew points at.
$peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
$machine = [System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x014C) {
    throw "$DllPath is machine 0x{0:X4}, not x86 (0x014C) - Far Cry 2 is a 32-bit process and cannot load it. Use the x86-$Config preset." -f $machine
}

# Latin-1 maps one byte to one char, so a byte search is a plain string search.
$image = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)

$crt = @("MSVCP140", "VCRUNTIME140", "api-ms-win-crt-", "ucrtbase") |
       Where-Object { $image.Contains($_) }
if ($crt) {
    throw "$DllPath links the dynamic CRT (found: $($crt -join ', ')) - it must build /MT. See the CMP0091 note in CMakeLists.txt."
}

if (-not $image.Contains("FCSE_Load")) {
    throw "$DllPath does not export FCSE_Load - FCSE loads a plugin by that one name and skips any DLL without it. Check the extern `"C`" __declspec(dllexport) on it in src\main.cpp."
}

Write-Host "LegacyPlugin.dll ($Config): x86, static CRT, exports FCSE_Load." -ForegroundColor Green
