#pragma once

#include <cstdint>
#include <string>
#include <windows.h>

// Which Dunia.dll build the running game is, decided from the *mapped image*.
//
// Far Cry 2 v1.03 ships as two distinct PC builds whose Dunia.dll images place
// the same code at different addresses, so every hardcoded address in FCSE is
// meaningless until this question is answered. Pre-1.03 builds are recognised
// on purpose rather than falling through to Unknown: a player on an unpatched
// install deserves "install the 1.03 patch", not "your game is unidentifiable".
namespace FCSE {

enum class DuniaBuild {
    Unknown,        // not a build this loader knows about
    Retail103,      // fc2_103_retail  - GOG (Fortune's Edition) / patched retail
    Uplay103,       // fc2_103_uplay   - Steam / Ubisoft Connect re-release
    Retail100,      // fc2_100_retail  - unpatched retail/DVD 1.00, not supported
};

struct BuildInfo {
    DuniaBuild build = DuniaBuild::Unknown;
    bool supported = false;

    // What was measured, kept verbatim so a bug report from an unknown build is
    // actionable: these two numbers are all it takes to add it to the table.
    uint32_t timeDateStamp = 0;
    uint32_t sizeOfImage = 0;

    const char* id = "unknown";          // e.g. "fc2_103_uplay"
    const char* label = "unrecognised";  // human-facing
    std::string reason;                  // why an unsupported build was refused
};

// Reads the COFF/optional headers out of the already-mapped module.
//
// Deliberately *not* a file hash. Two of the sampled DLLs have different
// SHA-256s and are the same image byte for byte, differing only in the PE
// CheckSum field and an Authenticode blob appended past the last section -
// neither of which is mapped into memory. A file-hash gate would reject a
// legitimate install for being re-signed.
BuildInfo IdentifyDuniaBuild(HMODULE duniaModule);

const char* ToString(DuniaBuild build);

} // namespace FCSE
