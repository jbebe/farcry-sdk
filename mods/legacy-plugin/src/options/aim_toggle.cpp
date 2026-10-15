// Aim toggle: a tap on aim stays in the sights until the next press; holding aims while held.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/input/inputtoggles.ixx, its aim half,
// for mouse and pad alike. Three sites, in the player controller: the ironsight press and release
// handlers, the release skipping the bit clear while latched, and the per-frame drop of actions
// no longer in the action map, which ends the latch when the sights come down on their own.
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>

namespace {
    // Player controller -> pawn -> state block, whose flags for what this frame asked for are
    // cleared every frame.
    constexpr ptrdiff_t kControllerPawn = 0x20;
    constexpr ptrdiff_t kPawnStateBlock = 0x10;
    constexpr ptrdiff_t kInputStateFlags = 0x144;
    constexpr uint8_t kIronsightFlag = 0x08;

    // A press shorter than this latches.
    constexpr ULONGLONG kTapMilliseconds = 250;

    // Into the matches: the release's call, and the join past its bit clear.
    constexpr ptrdiff_t kReleaseCall = 2;
    constexpr ptrdiff_t kReleaseJoin = 11;

    // Before OR byte [EAX+4],8.
    FCSE::Relocation<uint8_t*> g_press{
        FCSE::Pattern("E8 ?? ?? ?? ?? 80 48 04 08 5F 5E 83 C4 10 C2 08 00")};

    // Before AND byte [EAX+4],0F7h.
    FCSE::Relocation<uint8_t*> g_release{
        FCSE::Pattern("75 11 E8 ?? ?? ?? ?? 80 60 04 F7 5F 5E 83 C4 10 C2 08 00")};

    // AL is whether ironsight is bound in this frame's action map.
    FCSE::Relocation<uint8_t*> g_mapDrop{
        FCSE::Pattern("84 C0 75 0C 8B 4E 20 E8 ?? ?? ?? ?? 80 60 04 F7")};

    bool g_enabled = false;

    bool g_latched = false;
    bool g_pressActive = false;
    bool g_pressedWhileLatched = false;
    ULONGLONG g_pressTime = 0;

    uintptr_t StateBlock(uintptr_t controller) {
        const uintptr_t pawn = *reinterpret_cast<uintptr_t*>(controller + kControllerPawn);
        return pawn == 0 ? 0 : *reinterpret_cast<uintptr_t*>(pawn + kPawnStateBlock);
    }

    void PressHandler(FCSE_MidHookContext* /*ctx*/) {
        if (!g_enabled) {
            g_latched = false;
            g_pressActive = false;
            return;
        }

        if (g_pressActive) {
            return;
        }

        // A press while latched drops the latch, tap or hold.
        g_pressActive = true;
        g_pressTime = GetTickCount64();
        g_pressedWhileLatched = g_latched;
    }

    void ReleaseHandler(FCSE_MidHookContext* ctx) {
        if (!g_enabled) {
            g_latched = false;
            g_pressActive = false;
            return;
        }

        // A release with no press is a repeat, swallowed while latched.
        if (g_pressActive) {
            g_pressActive = false;
            g_latched =
                !g_pressedWhileLatched && GetTickCount64() - g_pressTime <= kTapMilliseconds;
        }

        if (g_latched) {
            ctx->eip = g_release.address() + kReleaseJoin;
        }
    }

    // ESI is the controller.
    void MapDropHandler(FCSE_MidHookContext* ctx) {
        if (!g_latched) {
            return;
        }

        const uintptr_t block = ctx->esi == 0 ? 0 : StateBlock(ctx->esi);
        if ((ctx->eax & 0xFF) == 0 || block == 0 ||
            (*reinterpret_cast<uint8_t*>(block + kInputStateFlags) & kIronsightFlag) == 0) {
            g_latched = false;
            g_pressActive = false;
        }
    }
}

void InstallAimToggle() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_press || !g_release || !g_mapDrop) {
        api->Log("aim toggle: the ironsight handlers were not all found in this build - not "
                 "applied");
        return;
    }

    api->MidHook(g_press.get(), &PressHandler);
    api->MidHook(g_release.get() + kReleaseCall, &ReleaseHandler);
    api->MidHook(g_mapDrop.get(), &MapDropHandler);
}

void __cdecl OnAimToggleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_enabled = value->asCheckbox;
}
