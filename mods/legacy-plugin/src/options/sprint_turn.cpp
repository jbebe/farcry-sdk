// Sprint turn: on, sprinting no longer lowers horizontal look speed.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/input/looksensitivity.ixx, its
// Sprint Turn Modifier at 1.0. Two sites: CPawnInputListener's look handler and the sprint
// start each multiply the yaw by the archetype's sprint modifier, which on replaces with 1.0.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // Into both matches: the multiply, with the modifier loaded into XMM0.
    constexpr ptrdiff_t kModifierLoaded = 8;

    // JackalFix's single pattern matches both sites; the instruction after it tells them apart.
    FCSE::Relocation<uint8_t*> g_look{FCSE::Pattern(
        "F3 0F 10 80 A4 00 00 00 F3 0F 59 46 14 F3 0F 11 46 14 F3 0F 10 56 10")};
    FCSE::Relocation<uint8_t*> g_sprintStart{FCSE::Pattern(
        "F3 0F 10 80 A4 00 00 00 F3 0F 59 46 14 F3 0F 11 46 14 8B 4E 20 E8")};

    bool g_enabled = false;

    void ModifierHandler(FCSE_MidHookContext* ctx) {
        if (g_enabled) {
            ctx->xmm0.f32[0] = 1.0f;
        }
    }
}

void InstallSprintTurn() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_look || !g_sprintStart) {
        api->Log("sprint turn: the sprint yaw modifier sites were not both found in this build - "
                 "not applied");
        return;
    }

    api->MidHook(g_look.get() + kModifierLoaded, &ModifierHandler);
    api->MidHook(g_sprintStart.get() + kModifierLoaded, &ModifierHandler);
}

void __cdecl OnSprintTurnChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_enabled = value->asCheckbox;
}
