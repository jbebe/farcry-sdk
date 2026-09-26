// Mutes: silences a category of weapon sound by handing its PlaySound call no sound id, which the engine
// already plays as nothing. Each hook sits on the `push type; push id; mov ecx, eax; call edx` before
// the call, with the id still in a register.
#include "mutes.h"

#include "fcse_api.h"

#include "imgui.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <iterator>

namespace {
    constexpr uint32_t kNoSound = 0xFFFFFFFF;
    constexpr uintptr_t kFireProperties = 0x50;
    constexpr uintptr_t kStartAutoEcho = 0x498;

    // ApplyDelayBullet: the player's single shot (ECX; strategy in EDI), before the echo from [ECX+0x490].
    FCSE::Relocation<uint8_t*> g_playerShot{FCSE::Pattern(
        "56 51 8B C8 FF D2 E8 ?? ?? ?? ?? D9 05 ?? ?? ?? ?? 8B 4F 50 8B 30 51 D9 1C 24 6A 00 8D 53 04 52 "
        "8B 91 58 04 00 00 8B 89 90 04 00 00")};

    // ApplyDelayBullet: everyone else's single shot (ECX).
    FCSE::Relocation<uint8_t*> g_npcShot{FCSE::Pattern(
        "56 51 8B C8 FF D2 8B 44 24 18 50 8B CF E8 ?? ?? ?? ?? 8B 4C 24 18 E8 ?? ?? ?? ?? F6 40 04 80 0F 85")};

    // StartUse: the player's fire loop (ECX), before the start echo from [ECX+0x498].
    FCSE::Relocation<uint8_t*> g_playerLoop{FCSE::Pattern(
        "57 51 8B C8 FF D2 89 86 0C 01 00 00 E8 ?? ?? ?? ?? D9 EE 8B 4E 50 8B B9 64 04 00 00 51 "
        "8B 89 98 04 00 00")};

    // StartUse: everyone else's fire loop, or the player's start echo (ECX; strategy in ESI).
    FCSE::Relocation<uint8_t*> g_npcLoopOrStartEcho{FCSE::Pattern(
        "57 51 8B C8 FF D2 F6 05 ?? ?? ?? ?? 01 BF ?? ?? ?? ?? BB 0F 00 00 00 75")};

    // PreStopUse: the player's fire-loop tail (EAX), before the stop echo from [ESI+0x49C].
    FCSE::Relocation<uint8_t*> g_playerTail{FCSE::Pattern(
        "51 50 8B CF FF D2 E8 ?? ?? ?? ?? D9 EE 8B 76 50 8B 10 8B 92 9C 00 00 00 51 8B 8E 64 04 00 00 "
        "D9 1C 24 6A 00 51 8B 8E 9C")};

    // StopUse: everyone else's stop event (ECX; strategy in ESI), which stops their loop and plays its tail.
    FCSE::Relocation<uint8_t*> g_npcStop{FCSE::Pattern(
        "57 51 8B C8 FF D2 A1 ?? ?? ?? ?? 80 78 04 00 74 ?? 8D 4C 24 18 E8")};

    // Fire strategies whose loop start was muted: only their stop event may be muted too, or a loop started
    // before muting would never be stopped.
    std::array<uintptr_t, 32> g_mutedLoops{};
    size_t g_nextMutedLoop = 0;

    void OnSingleShot(FCSE_MidHookContext* ctx) {
        if (SoundOverhaul::Mutes::singleShots) {
            ctx->ecx = kNoSound;
        }
    }

    void OnPlayerLoop(FCSE_MidHookContext* ctx) {
        if (SoundOverhaul::Mutes::autoFire) {
            ctx->ecx = kNoSound;
        }
    }

    void OnNpcLoopOrStartEcho(FCSE_MidHookContext* ctx) {
        const uintptr_t properties = *reinterpret_cast<const uintptr_t*>(ctx->esi + kFireProperties);
        if (ctx->ecx == *reinterpret_cast<const uint32_t*>(properties + kStartAutoEcho)) {
            if (SoundOverhaul::Mutes::echoes) {
                ctx->ecx = kNoSound;
            }
        } else if (SoundOverhaul::Mutes::autoFire && ctx->ecx != kNoSound) {
            ctx->ecx = kNoSound;
            g_mutedLoops[g_nextMutedLoop++ % g_mutedLoops.size()] = ctx->esi;
        }
    }

    void OnPlayerTail(FCSE_MidHookContext* ctx) {
        if (SoundOverhaul::Mutes::autoFire) {
            ctx->eax = kNoSound;
        }
    }

    void OnNpcStop(FCSE_MidHookContext* ctx) {
        const auto muted = std::find(g_mutedLoops.begin(), g_mutedLoops.end(), ctx->esi);
        if (muted != g_mutedLoops.end()) {
            *muted = 0;
            ctx->ecx = kNoSound;
        }
    }

    struct Site {
        const char* name;
        FCSE::Relocation<uint8_t*>* target;
        FCSE_MidHookHandler handler;
    };
}

void SoundOverhaul::Mutes::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const Site sites[] = {
        {"player single shot", &g_playerShot, &OnSingleShot},
        {"NPC single shot", &g_npcShot, &OnSingleShot},
        {"player fire loop", &g_playerLoop, &OnPlayerLoop},
        {"NPC fire loop", &g_npcLoopOrStartEcho, &OnNpcLoopOrStartEcho},
        {"player fire-loop tail", &g_playerTail, &OnPlayerTail},
        {"NPC stop event", &g_npcStop, &OnNpcStop},
    };
    for (const Site& site : sites) {
        if (!*site.target || !api->MidHook(site.target->get(), site.handler)) {
            FCSE::Logf("mutes: the %s was not found in this build - it cannot be muted", site.name);
        }
    }
}

void SoundOverhaul::Mutes::DrawWindow(void*) {
    ImGui::TextDisabled("Mute a kind of weapon sound to hear the rest of a fight.");
    ImGui::Checkbox("Mute single shots", &singleShots);
    ImGui::Checkbox("Mute full-auto fire", &autoFire);
    ImGui::Checkbox("Mute echoes", &echoes);
}
