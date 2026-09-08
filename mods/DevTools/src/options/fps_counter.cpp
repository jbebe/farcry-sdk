// FPS counter.
//
// The engine draws its own frame rate readout when the global behind the `showFps` console variable
// is non-zero, and nothing in the game writes that global except the console. So this needs no hook:
// it finds the global and writes it.
//
// The `showFps` catalog row reaches the same global, so this is the shorter road to it rather than
// a different capability: a checkbox in the overlay instead of typing a command.
//
// The console's own help text for `showFps` reads "0 (on) or 1 (off)". It is backwards; non-zero
// draws.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The CMP that tests the global carries nothing but its relocated address, so the pattern reaches
    // back to the FSTP before it and on through the two globals that follow to be unique.
    FCSE::Relocation<uint8_t*> g_showFpsTest{FCSE::Pattern(
        "D9 5C 24 0C 83 3D ?? ?? ?? ?? 00 74 ?? 83 3D ?? ?? ?? ?? 00 8B 35 ?? ?? ?? ?? 75 07 33 C9")};

    constexpr size_t kShowFpsOperand = 6;

    bool g_enabled = false;
}

int GetFpsCounter() { return g_enabled ? 1 : 0; }

void SetFpsCounter(int value) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_showFpsTest) {
        api->Log("fps counter: the showFps global was not found in this build - no readout");
        return;
    }

    int32_t* showFps = *reinterpret_cast<int32_t**>(g_showFpsTest.get() + kShowFpsOperand);
    g_enabled = value != 0;
    *showFps = g_enabled ? 1 : 0;

    api->Log(g_enabled ? "fps counter: on" : "fps counter: off");
}
