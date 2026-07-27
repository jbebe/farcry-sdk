#pragma once

#include <cstdint>
#include <string>

// The stock MalariaCurve and PlayerSPFinalize handlers (see debug_commands.h) each multiply/write
// a fixed constant baked into FarCry2.exe's own data section (0x4020fc and 0x402100 - see
// docs/docs/engine-internals/launcher-exe.md, both noted there as "not yet read/typed"). Rather
// than hardcode a guessed value in source, this reads the real bytes straight out of the actual
// FarCry2.exe shipped next to this loader - keeps the reimplementation byte-for-byte faithful to
// whatever the installed game build actually contains, without asserting a specific number here
// that was never confirmed against a live binary.
namespace FCSE {

struct StockConstants {
    // Fallback values (used only if FarCry2.exe can't be found/mapped) are chosen to be inert
    // rather than a guess at the real value: 1.0f is a no-op multiplier for MalariaCurve, 0 is
    // PlayerSPFinalize's simplest not-holding-a-guessed-status-code default.
    float malariaCurveMultiplier = 1.0f;
    int32_t playerSPFinalizeValue = 0;
    bool resolvedFromRealExe = false;
};

// Maps FarCry2.exe (expected next to `directory`, i.e. bin\) with DONT_RESOLVE_DLL_REFERENCES (no
// DllMain execution, no import resolution - just gets the sections placed at their real
// RVA-relative offsets from the returned base) and reads the two constants by VA. VA-to-offset
// arithmetic is relative to the PE's own preferred image base (0x400000, confirmed non-relocated
// in docs/docs/engine-internals/overview.md), so this is correct even if Windows happens to load
// the mapping at a different actual address. Logs a warning and returns the inert fallback values
// above if FarCry2.exe isn't present/mappable.
StockConstants LoadStockConstants(const std::wstring& directory);

} // namespace FCSE
