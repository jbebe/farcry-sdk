// Last echo: of the echoes a burst plays, one per round, only the last rings out. Each new round's echo
// fades out the previous one, so the burst ends with one echo however it ends - release, empty magazine
// or reload.
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>

namespace {
    // ApplyDelayBullet storing the handle of the player's echo (EAX), before it fetches the sound system
    // and stops the echo over GetEchoLength().
    FCSE::Relocation<uint8_t*> g_echoPlayed{FCSE::Pattern(
        "89 44 24 30 E8 ?? ?? ?? ?? 8B 30 8B 0D ?? ?? ?? ?? 6A 00 89 44 24 60 81 C6 A8 00 00 00 E8")};

    // The `call GetSoundSystem` right after the hooked `mov`.
    constexpr uintptr_t kGetSoundSystemCall = 4;
    constexpr uintptr_t kStopSound = 0xA8;
    // Echoes closer together than this belong to one burst.
    constexpr ULONGLONG kBurstGapMs = 350;
    constexpr float kCutFadeSeconds = 0.03f;

    using GetSoundSystemFn = void* (*)();
    using StopSoundFn = void(__thiscall*)(void* system, uint32_t handle, float fade, uint32_t flags);

    GetSoundSystemFn g_getSoundSystem = nullptr;
    uintptr_t g_strategy = 0;
    uint32_t g_echo = 0;
    ULONGLONG g_echoTime = 0;

    void OnEchoPlayed(FCSE_MidHookContext* ctx) {
        const ULONGLONG now = GetTickCount64();
        const bool sameBurst = ctx->edi == g_strategy && now - g_echoTime < kBurstGapMs;
        if (sameBurst && g_echo != 0 && g_echo != 0xFFFFFFFF) {
            void* system = g_getSoundSystem();
            const auto stop = reinterpret_cast<StopSoundFn>(
                (*reinterpret_cast<void***>(system))[kStopSound / sizeof(void*)]);
            stop(system, g_echo, kCutFadeSeconds, 0);
        }

        g_strategy = ctx->edi;
        g_echo = static_cast<uint32_t>(ctx->eax);
        g_echoTime = now;
    }
}

void ApplyLastEcho() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_echoPlayed) {
        api->Log("last echo: ApplyDelayBullet's echo was not found in this build");
        return;
    }

    const uint8_t* call = g_echoPlayed.get() + kGetSoundSystemCall;
    g_getSoundSystem = reinterpret_cast<GetSoundSystemFn>(call + 5 + *reinterpret_cast<const int32_t*>(call + 1));

    if (api->MidHook(g_echoPlayed.get(), &OnEchoPlayed)) {
        api->Log("last echo: a burst's echo rings out only after its last round");
    }
}
