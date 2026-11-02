// Several copies of the game at once.
//
// The symptom: launching the game while a copy is already running starts nothing.
//
// InitDuniaEngine opens the named mutex FarCry2Instance and refuses to start when it exists
// (Steam 0x10004D89 / GOG 0x10004DA9):
//
//     68 01 00 1F 00       push MUTEX_ALL_ACCESS
//     FF 15 ?? ?? ?? ??    call [OpenMutexA]
//     85 C0                test eax, eax
//     0F 85 ?? ?? ?? ??    jnz  <refuse>
//
// NOPping the jnz always takes the path that creates the mutex, which discards the handle and never
// asks whether it already existed, so each copy starts.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The jnz, 13 into the match.
    constexpr ptrdiff_t kRefuseJump = 13;

    constexpr uint8_t kNopJump[] = {0x90, 0x90, 0x90, 0x90, 0x90, 0x90};

    FCSE::Relocation<uint8_t*> g_instanceCheck{
        FCSE::Pattern("68 01 00 1F 00 FF 15 ?? ?? ?? ?? 85 C0 0F 85")};
}

void ApplyMultipleInstancesFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_instanceCheck) {
        api->Log("multiple instances: the engine's instance check was not found in this build - one "
                 "copy at a time");
        return;
    }

    if (api->Patch(g_instanceCheck.get() + kRefuseJump, kNopJump, sizeof(kNopJump))) {
        api->Log("multiple instances: another copy of the game may start alongside this one");
    }
}
