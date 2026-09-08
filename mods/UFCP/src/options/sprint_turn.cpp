// Turning is quietly slowed while sprinting.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - the sprint-turn half of
// source/input/looksensitivity.ixx.
//
// Sprinting multiplies the yaw rate by the archetype's fSprintingTurnModifier, so the view turns
// noticeably less far for the same input while running. Nothing announces it and no option exposes
// it, which is why it reads as the mouse sticking rather than as a deliberate limit.
//
// It is an option and not a fix: the modifier is an authored value the console builds have too, so
// this is a feel decision players disagree about rather than something the PC build gets wrong. At
// 0 the engine's own value is passed through untouched.
//
// The modifier is applied at two sites - once on entering a sprint and once while one continues -
// and the load is byte-identical at both:
//
//     F3 0F 10 80 A4 00 00 00   movss xmm0, dword ptr [eax+0A4h]   ; fSprintingTurnModifier
//     F3 0F 59 46 14            mulss xmm0, dword ptr [esi+14h]
//
// FCSE reports a pattern matching twice as no match at all, so each site carries the instructions
// that follow it: the continuing case reads the turn rate next, the entering case calls back into
// the controller and sets the sprint bit. Both extensions resolve exactly once on both builds.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // Where the modifier has been loaded into xmm0, past the movss.
    constexpr ptrdiff_t kModifierLoaded = 8;

    FCSE::Relocation<uint8_t*> g_duringSprint{
        FCSE::Pattern("F3 0F 10 80 A4 00 00 00 F3 0F 59 46 14 F3 0F 11 46 14 "
                      "F3 0F 10 56 10 F3 0F 10 05")};

    FCSE::Relocation<uint8_t*> g_enterSprint{
        FCSE::Pattern("F3 0F 10 80 A4 00 00 00 F3 0F 59 46 14 F3 0F 11 46 14 "
                      "8B 4E 20 E8 ?? ?? ?? ?? 80 48 04 40 EB 07 8B CE")};

    // 0 means "leave the archetype's own value alone".
    float g_modifier = 0.0f;

    void ModifierHandler(FCSE_MidHookContext* ctx) {
        if (g_modifier > 0.0f) {
            ctx->xmm0.f32[0] = g_modifier;
        }
    }
}

void InstallSprintTurnHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_duringSprint || !g_enterSprint) {
        api->Log("sprint turn: the sprinting turn modifier was not found in this build - the turn "
                 "rate cannot be changed");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_duringSprint.address() + kModifierLoaded),
                 &ModifierHandler);
    api->MidHook(reinterpret_cast<void*>(g_enterSprint.address() + kModifierLoaded),
                 &ModifierHandler);
}

void __cdecl OnSprintTurnChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    // The checkbox asks for the full turn rate, which is the modifier at 1.
    g_modifier = value->asCheckbox ? 1.0f : 0.0f;
}
