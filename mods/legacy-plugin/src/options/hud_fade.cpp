// HUD fade-out: how long the health, ammo and equipment counters stay up, or always hidden/shown.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/hudfadeout.ixx. The choice
// indexes kHudFadeChoices in main.cpp. Sites, all in CFCXMainHudUI: the ammo and grenade groups'
// countdown reads in both arms of each state machine, the health bar's show flag, the weapon-draw
// show animation, and two ammo fade-in entries stubbed to a bare return while always hidden.
#include "fcse_api.h"

#include <cstdint>
#include <iterator>

namespace {
    // CFCXMainHudUI: the fade-out delay every countdown is topped up to, and each group's state.
    constexpr ptrdiff_t kFadeDelay = 0x250;
    constexpr ptrdiff_t kAmmoState = 0xAC;
    constexpr ptrdiff_t kGrenadeState = 0xB0;
    constexpr uint32_t kStateShown = 2;

    constexpr int kAlwaysHidden = -2;
    constexpr int kAlwaysShown = -1;
    constexpr int kStockDelay = 0;

    // JackalFix's HudFadeOut value for each choice in main.cpp: seconds, or one of the modes above.
    constexpr int kModes[] = {kAlwaysHidden, 1,  2,  kStockDelay, 5,  8,
                              10,            15, 20, 30,          60, kAlwaysShown};

    // Never counts down to zero within a session.
    constexpr float kNeverFades = 1.0e9f;

    // Under any frame time, so the next tick takes the hide path.
    constexpr float kHideNow = 1.0e-6f;

    // Into the matches: each countdown read, past the tick's last top-up; the health bar's flag
    // test; the weapon draw's timer read and the end of its show animation.
    constexpr ptrdiff_t kAmmoRead = 7;
    constexpr ptrdiff_t kGrenadeRead = 8;
    constexpr ptrdiff_t kHealthFlag = 7;
    constexpr ptrdiff_t kDrawTimer = 8;
    constexpr ptrdiff_t kDrawSkip = 0x53;

    // The hidden arm tests the countdown against zero, the shown arm counts it down.
    FCSE::Relocation<uint8_t*> g_ammoHidden{
        FCSE::Pattern("C6 86 73 02 00 00 00 F3 0F 10 03 0F 2E 05")};
    FCSE::Relocation<uint8_t*> g_ammoShown{
        FCSE::Pattern("C6 86 73 02 00 00 00 F3 0F 10 03 0F 57 C9")};
    FCSE::Relocation<uint8_t*> g_grenadeHidden{
        FCSE::Pattern("53 8B CE E8 ?? ?? ?? ?? F3 0F 10 03 0F 2E 05")};
    FCSE::Relocation<uint8_t*> g_grenadeShown{
        FCSE::Pattern("53 8B CE E8 ?? ?? ?? ?? F3 0F 10 03 0F 57 C9")};
    FCSE::Relocation<uint8_t*> g_health{FCSE::Pattern("C6 86 64 02 00 00 00 84 D2 74")};
    FCSE::Relocation<uint8_t*> g_draw{
        FCSE::Pattern("F3 0F 11 86 54 02 00 00 F3 0F 10 86 58 02 00 00")};
    FCSE::Relocation<uint8_t*> g_bulletFadeIn{
        FCSE::Pattern("83 EC 0C 56 8B F1 57 8B 7E 04 85 FF 0F 84 BE 00 00 00 E8 ?? ?? ?? ?? "
                      "83 F8 03 0F 87 84 00 00 00 FF 24 85 ?? ?? ?? ?? 8B 46 08")};
    FCSE::Relocation<uint8_t*> g_rocketFadeIn{
        FCSE::Pattern("56 8B F1 E8 ?? ?? ?? ?? 8B 4E 2C 85 C9 74 19 6A 00 6A 01 6A 00")};

    int g_mode = kStockDelay;
    bool g_installed = false;

    // The delay the HUD data shipped, read before the first write over it.
    float g_stockDelay = 0.0f;

    // Both fade-in entries take only this, so a bare return balances.
    constexpr uint8_t kReturn = 0xC3;
    uint8_t g_bulletEntry = 0;
    uint8_t g_rocketEntry = 0;

    // EBX is the group's countdown, ESI the HUD.
    void ApplyFadeOut(FCSE_MidHookContext* ctx, ptrdiff_t state) {
        float* delay = reinterpret_cast<float*>(ctx->esi + kFadeDelay);
        float* countdown = reinterpret_cast<float*>(ctx->ebx);

        if (g_stockDelay == 0.0f) {
            g_stockDelay = *delay;
        }

        switch (g_mode) {
        case kAlwaysHidden:
            // Zero holds a hidden group down; a group still up needs one tick left to run its hide.
            *countdown = *reinterpret_cast<uint32_t*>(ctx->esi + state) == kStateShown ? kHideNow
                                                                                        : 0.0f;
            break;
        case kAlwaysShown:
            *countdown = kNeverFades;
            break;
        default:
            *delay = g_mode > 0 ? static_cast<float>(g_mode) : g_stockDelay;

            // A shortened delay bites this tick rather than waiting for the next top-up.
            if (*countdown > *delay) {
                *countdown = *delay;
            }
            break;
        }
    }

    void AmmoHandler(FCSE_MidHookContext* ctx) {
        ApplyFadeOut(ctx, kAmmoState);
    }

    void GrenadeHandler(FCSE_MidHookContext* ctx) {
        ApplyFadeOut(ctx, kGrenadeState);
    }

    // DL tops the health bar's countdown up, CL zeroes it.
    void HealthHandler(FCSE_MidHookContext* ctx) {
        if (g_mode >= 0) {
            return;
        }

        ctx->edx = (ctx->edx & ~0xFFu) | (g_mode == kAlwaysShown ? 1u : 0u);
        ctx->ecx = (ctx->ecx & ~0xFFu) | (g_mode == kAlwaysHidden ? 1u : 0u);
    }

    // Drawing a weapon plays the grenade group's show animation itself, around the state machine.
    void DrawHandler(FCSE_MidHookContext* ctx) {
        if (g_mode == kAlwaysHidden) {
            ctx->eip = g_draw.address() + kDrawSkip;
        }
    }

    void SetFadeIns(bool stubbed) {
        const FCSE_PluginAPI* api = FCSE::ApiPointer();
        api->Patch(g_bulletFadeIn.get(), stubbed ? &kReturn : &g_bulletEntry, 1);
        api->Patch(g_rocketFadeIn.get(), stubbed ? &kReturn : &g_rocketEntry, 1);
    }
}

void InstallHudFade() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_ammoHidden || !g_ammoShown || !g_grenadeHidden || !g_grenadeShown || !g_health ||
        !g_draw || !g_bulletFadeIn || !g_rocketFadeIn) {
        api->Log("HUD fade-out: the main HUD's fade sites were not all found in this build - not "
                 "applied");
        return;
    }

    g_bulletEntry = *g_bulletFadeIn.get();
    g_rocketEntry = *g_rocketFadeIn.get();

    api->MidHook(g_ammoHidden.get() + kAmmoRead, &AmmoHandler);
    api->MidHook(g_ammoShown.get() + kAmmoRead, &AmmoHandler);
    api->MidHook(g_grenadeHidden.get() + kGrenadeRead, &GrenadeHandler);
    api->MidHook(g_grenadeShown.get() + kGrenadeRead, &GrenadeHandler);
    api->MidHook(g_health.get() + kHealthFlag, &HealthHandler);
    api->MidHook(g_draw.get() + kDrawTimer, &DrawHandler);
    g_installed = true;
}

void __cdecl OnHudFadeChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const int mode = value->asChoice < std::size(kModes) ? kModes[value->asChoice] : kStockDelay;

    if (g_installed && (mode == kAlwaysHidden) != (g_mode == kAlwaysHidden)) {
        SetFadeIns(mode == kAlwaysHidden);
    }
    g_mode = mode;
}
