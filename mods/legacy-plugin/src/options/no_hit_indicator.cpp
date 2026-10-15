// No hit indicator: taking damage no longer shows the directional hit indicators.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/hitindicator.ixx. One site,
// in the player-damaged handler: the hook at the head of the loop raising the indicators jumps to
// its exhausted path, so the reference release after it still runs.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // Into the match: the loop head, and the loop's exhausted path.
    constexpr ptrdiff_t kLoopHead = 14;
    constexpr ptrdiff_t kLoopExhausted = 0x75;

    FCSE::Relocation<uint8_t*> g_damaged{
        FCSE::Pattern("F3 0F 10 00 F3 0F 10 48 04 F3 0F 10 50 08 33 FF 8D 83 20 02 00 00")};

    bool g_enabled = false;

    void LoopHeadHandler(FCSE_MidHookContext* ctx) {
        if (g_enabled) {
            ctx->eip = g_damaged.address() + kLoopExhausted;
        }
    }
}

void InstallNoHitIndicator() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_damaged) {
        api->Log("no hit indicator: the player-damaged handler was not found in this build - not "
                 "applied");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_damaged.address() + kLoopHead), &LoopHeadHandler);
}

void __cdecl OnNoHitIndicatorChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_enabled = value->asCheckbox;
}
