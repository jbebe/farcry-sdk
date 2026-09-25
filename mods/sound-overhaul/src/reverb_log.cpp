// Reverb log: every reverb DARE switches to, as a line in fcse.log. A diagnostic, to be removed.
#include "fcse_api.h"

#include <cstdint>

namespace {
    using SetReverbFn = void(__cdecl*)(uint32_t effect);

    // DARE_SetReverb(effect), which every type-8 reverb event reaches.
    FCSE::Relocation<SetReverbFn> g_setReverb{FCSE::Pattern(
        "A1 ?? ?? ?? ?? 85 C0 75 2A 50 50 50 50 50 50 50 6A 04 68 B4 00 00 00 E8 ?? ?? ?? ?? "
        "83 C4 24 85 C0 74 09 8B C8 E8 ?? ?? ?? ?? EB 02 33 C0 A3 ?? ?? ?? ?? 56 8B 74 24 08 56 "
        "8B C8 E8 ?? ?? ?? ?? 89 35 ?? ?? ?? ?? 5E C3")};

    SetReverbFn g_original = nullptr;

    void __cdecl OnSetReverb(uint32_t effect) {
        FCSE::Logf("reverb: 0x%08X", effect);
        g_original(effect);
    }
}

void InstallReverbLog() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_setReverb) {
        api->Log("reverb log: DARE_SetReverb was not found in this build");
        return;
    }

    api->Hook(g_setReverb.get(), reinterpret_cast<void*>(&OnSetReverb),
              reinterpret_cast<void**>(&g_original));
}
