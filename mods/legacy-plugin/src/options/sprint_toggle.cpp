// Sprint toggle: a tap on sprint keeps running until the next press or the player stops. Keyboard
// only - the pad's sprint already latches, having no release.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/input/inputtoggles.ixx, its sprint
// half. Three sites, in the player controller: the sprint press and release handlers, the release
// skipping the bit clear and end of run while latched, and the per-frame drop of actions no longer
// in the action map, which ends the latch once the run has ended on its own.
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>

namespace {
    // Player controller -> pawn -> state block, whose flags for what the pawn is doing now persist
    // across frames.
    constexpr ptrdiff_t kControllerPawn = 0x20;
    constexpr ptrdiff_t kPawnStateBlock = 0x10;
    constexpr ptrdiff_t kCurrentStateFlags = 0x2D4;
    constexpr uint8_t kSprintFlag = 0x40;

    // The keyboard's sprint signal; the pad's is "sprint", which has no release.
    constexpr uint32_t kKeyboardSprint = FCSE::Crc32("lock_sprint");

    // A press shorter than this latches.
    constexpr ULONGLONG kTapMilliseconds = 250;

    // Into the matches: the release's call, and the join past its bit clear and end of run.
    constexpr ptrdiff_t kReleaseCall = 2;
    constexpr ptrdiff_t kReleaseJoin = 18;

    // Before OR byte [EAX+4],40h; EAX is the signal, every frame the key is held.
    FCSE::Relocation<uint8_t*> g_press{
        FCSE::Pattern("E8 ?? ?? ?? ?? 80 48 04 40 5F 5E 83 C4 10 C2 08 00")};

    // Before AND byte [EAX+4],0BFh and the end of run.
    FCSE::Relocation<uint8_t*> g_release{
        FCSE::Pattern("75 18 E8 ?? ?? ?? ?? 80 60 04 BF 8B CE E8 ?? ?? ?? ?? "
                      "5F 5E 83 C4 10 C2 08 00")};

    // AL is whether sprint is bound in this frame's action map.
    FCSE::Relocation<uint8_t*> g_mapDrop{
        FCSE::Pattern("84 C0 75 13 8B 4E 20 E8 ?? ?? ?? ?? 80 60 04 BF 8B CE")};

    bool g_enabled = false;

    bool g_latched = false;
    bool g_pressActive = false;
    bool g_pressedWhileLatched = false;
    ULONGLONG g_pressTime = 0;

    // From the controller, ESI at the release and the map drop.
    bool Sprinting(uintptr_t controller) {
        const uintptr_t pawn =
            controller == 0 ? 0 : *reinterpret_cast<uintptr_t*>(controller + kControllerPawn);
        const uintptr_t block =
            pawn == 0 ? 0 : *reinterpret_cast<uintptr_t*>(pawn + kPawnStateBlock);
        return block != 0 &&
               (*reinterpret_cast<uint8_t*>(block + kCurrentStateFlags) & kSprintFlag) != 0;
    }

    void PressHandler(FCSE_MidHookContext* ctx) {
        if (ctx->eax != kKeyboardSprint) {
            return;
        }

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

        // A release with no press is a repeat, swallowed while latched. Tapping while standing
        // still would arm a sprint that never ends.
        if (g_pressActive) {
            g_pressActive = false;
            g_latched = !g_pressedWhileLatched && Sprinting(ctx->esi) &&
                        GetTickCount64() - g_pressTime <= kTapMilliseconds;
        }

        if (g_latched) {
            ctx->eip = g_release.address() + kReleaseJoin;
        }
    }

    // The run ended on its own; sprinting again takes another tap.
    void MapDropHandler(FCSE_MidHookContext* ctx) {
        if (g_latched && ((ctx->eax & 0xFF) == 0 || !Sprinting(ctx->esi))) {
            g_latched = false;
            g_pressActive = false;
        }
    }
}

void InstallSprintToggle() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_press || !g_release || !g_mapDrop) {
        api->Log("sprint toggle: the sprint handlers were not all found in this build - not "
                 "applied");
        return;
    }

    api->MidHook(g_press.get(), &PressHandler);
    api->MidHook(g_release.get() + kReleaseCall, &ReleaseHandler);
    api->MidHook(g_mapDrop.get(), &MapDropHandler);
}

void __cdecl OnSprintToggleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_enabled = value->asCheckbox;
}
