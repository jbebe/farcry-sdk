// Echo log: every echo length the game trims a gunshot echo to, as a line in fcse.log. A diagnostic,
// to be removed.
#include "fcse_api.h"

#include <cstdint>

namespace {
    using GetEchoLengthFn = float(__thiscall*)(void* ambiance);

    // CAmbianceManager::GetEchoLength: the region's echo length, blended with the building's.
    FCSE::Relocation<GetEchoLengthFn> g_getEchoLength{FCSE::Pattern(
        "51 A1 ?? ?? ?? ?? F3 0F 10 48 14 0F 57 D2 0F 2F D1 72 2E F3 0F 10 05 ?? ?? ?? ?? F3 0F 5C C1 "
        "0F 2F C2 F3 0F 11 04 24 76 18 D9 E8 D9 04 24 DC E9 D9 C9 D8 89 AC 03 00 00")};

    GetEchoLengthFn g_original = nullptr;

    float __fastcall OnGetEchoLength(void* ambiance, void*) {
        const float length = g_original(ambiance);
        FCSE::Logf("echo length: %.2f s", length);
        return length;
    }
}

void InstallEchoLog() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_getEchoLength) {
        api->Log("echo log: CAmbianceManager::GetEchoLength was not found in this build");
        return;
    }

    api->Hook(g_getEchoLength.get(), reinterpret_cast<void*>(&OnGetEchoLength),
              reinterpret_cast<void**>(&g_original));
}
