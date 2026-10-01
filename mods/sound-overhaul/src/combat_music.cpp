// Combat music: a Yes/No setting. With it off, a music state of combat - chase, battle, fight or suspense -
// still takes over from the calm music but plays nothing, so a fight is heard without a score over it, and the
// calm music comes back when it ends.
#include "fcse_api.h"

#include <algorithm>
#include <array>
#include <cstdint>

namespace {
    // CMusicManager::SSet::SState::Start: the `mov eax, [edi+8]` loading the state's start sound (EDI = the state)
    // before it is stored and played; the hook sits on the next instruction, at +3.
    FCSE::Relocation<uint8_t*> g_start{FCSE::Pattern(
        "8B 47 08 66 0F 5A C0 F3 0F 11 47 10 8B 7C 24 0C 89 07")};
    // CMusicManager::SSet::SState::Stop: the same for its stop sound, from [edi+0xc].
    FCSE::Relocation<uint8_t*> g_stop{FCSE::Pattern(
        "8B 47 0C 66 0F 5A C0 F3 0F 11 47 14 F3 0F 10 05 ?? ?? ?? ?? F3 0F 11 47 1C")};
    constexpr uintptr_t kAfterLoad = 3;

    constexpr uintptr_t kPriority = 0x4;
    // music.xml's states by priority: 0 is the silent "Walk (safe)", 1-11 chase, battle, fight and suspense,
    // 12-16 fly, drive and walk.
    constexpr int32_t kFirstCombat = 1;
    constexpr int32_t kLastCombat = 11;
    constexpr uint32_t kNoSound = 0xFFFFFFFF;

    bool g_combatMusic = true;

    // The states whose start was silenced. Only theirs is their stop: a state's stop event is what ends its
    // music, so one that started playing - the setting turned off mid-fight - must still get its stop.
    std::array<uintptr_t, 32> g_silenced{};

    void OnStart(FCSE_MidHookContext* ctx) {
        const int32_t priority = *reinterpret_cast<const int32_t*>(ctx->edi + kPriority);
        if (g_combatMusic || priority < kFirstCombat || priority > kLastCombat) {
            return;
        }
        ctx->eax = kNoSound;
        if (std::find(g_silenced.begin(), g_silenced.end(), ctx->edi) == g_silenced.end()) {
            const auto free = std::find(g_silenced.begin(), g_silenced.end(), 0u);
            if (free != g_silenced.end()) {
                *free = ctx->edi;
            }
        }
    }

    void OnStop(FCSE_MidHookContext* ctx) {
        const auto silenced = std::find(g_silenced.begin(), g_silenced.end(), ctx->edi);
        if (silenced != g_silenced.end()) {
            *silenced = 0;
            ctx->eax = kNoSound;
        }
    }

    void OnChanged(const FCSE_SettingValue* value, void*) {
        g_combatMusic = value->asCheckbox;
    }
}

void ApplyCombatMusic() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const FCSE_Setting setting{"Combat music", FCSE_CHECKBOX(true), &OnChanged, nullptr};
    api->RegisterSettings("SoundOverhaul", &setting, 1);

    if (!g_start || !g_stop) {
        api->Log("combat music: the music state's start or stop was not found in this build - always on");
        return;
    }
    if (api->MidHook(g_start.get() + kAfterLoad, &OnStart) && api->MidHook(g_stop.get() + kAfterLoad, &OnStop)) {
        api->Log("combat music: follows its setting");
    }
}
