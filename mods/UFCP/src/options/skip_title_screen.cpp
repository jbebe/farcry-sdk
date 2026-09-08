// Skip the "press any key" title screen.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/gameplay/titlescreen.ixx.
//
// The main menu is built in states, and MAINMENU_INITIAL_PRESENTATION is the one that waits for a
// keypress before handing over to CFCXMainPage. The state is read as
//
//     8B 86 6C 01 00 00    mov eax, [esi+16Ch]
//     83 E8 01             sub eax, 1
//     74 ??                je  <the waiting state>
//
// Zeroing that jump's displacement makes the branch fall straight through into the state after it,
// so the menu appears without the wait. One byte.
//
// A preference rather than a fix: waiting for a keypress is what the game shipped doing, and the
// default here leaves it waiting.
#include "fcse_api.h"

#include "engine/toggle_patch.h"

#include <cstdint>

namespace {
    // The jump's displacement byte, 8 into the match.
    constexpr ptrdiff_t kWaitJumpDisplacement = 8;

    constexpr uint8_t kFallThrough[] = {0x00};

    FCSE::Relocation<uint8_t*> g_mainMenuState{
        FCSE::Pattern("8B 86 6C 01 00 00 83 E8 01 74 ?? 8B 86 68 01 00 00")};

    UFCP::TogglePatch g_patch{kFallThrough};
}

void InstallSkipTitleScreenHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_mainMenuState) {
        api->Log("skip title screen: the main menu's initial state was not found in this build - "
                 "the title screen cannot be skipped");
        return;
    }

    g_patch.Resolve(g_mainMenuState.address() + kWaitJumpDisplacement);
}

void __cdecl OnSkipTitleScreenChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_patch.Set(value->asCheckbox);
    FCSE::ApiPointer()->Log(value->asCheckbox ? "skip title screen: on" : "skip title screen: off");
}
