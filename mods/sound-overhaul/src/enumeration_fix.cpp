// Enumeration fix: the game starts with no sound through a Wine-derived DirectSound.
//
// DARE's device open (Steam 0x10A4C8F0, GOG 0x10A3B7C0) enumerates devices with a callback that
// stops at the first real one, then continues only if DirectSoundEnumerateA returned exactly 0.
// Windows returns DS_OK there; DSOAL before r689 returns S_FALSE, and the game shows its "Sound-Driver
// is currently used" box. The check's `jz` (Steam 0x10A4C913, GOG 0x10A3B7E3) becomes `jns`, so
// any success code passes and a real error still fails.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The enumeration call, its check and the failure path's `pop edi; or eax, -1; pop ebp`.
    FCSE::Relocation<uint8_t*> g_enumerateCheck{FCSE::Pattern(
        "E8 ?? ?? ?? ?? 85 C0 8D 4C 24 2C 74 10 E8 ?? ?? ?? ?? 5F 83 C8 FF 5D")};

    // The `jz` in the match above.
    constexpr ptrdiff_t kBranch = 11;

    constexpr uint8_t kJns = 0x79;
}

void ApplyEnumerationFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_enumerateCheck) {
        api->Log("enumeration fix: DARE's device open was not found in this build - not fixed");
        return;
    }

    if (api->Patch(reinterpret_cast<void*>(g_enumerateCheck.address() + kBranch), &kJns,
                   sizeof(kJns))) {
        api->Log("enumeration fix applied");
    }
}
