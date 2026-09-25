// Enumeration fix: DARE's device open accepts any success code from DirectSoundEnumerateA, not only
// DS_OK. Its `jz` (Steam 0x10A4C913, GOG 0x10A3B7E3) becomes `jns`.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The check's `jz`, then the failure path's call and `pop edi; or eax, -1; pop ebp`.
    FCSE::Relocation<uint8_t*> g_enumerateCheck{
        FCSE::Pattern("74 10 E8 ?? ?? ?? ?? 5F 83 C8 FF 5D")};

    constexpr uint8_t kJns = 0x79;
}

void ApplyEnumerationFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_enumerateCheck) {
        api->Log("enumeration fix: DARE's device open was not found in this build - not fixed");
        return;
    }

    if (api->Patch(g_enumerateCheck.get(), &kJns, sizeof(kJns))) {
        api->Log("enumeration fix applied");
    }
}
