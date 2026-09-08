// Raw key state, for the things the game's own input never sees.
#include "engine/keys.h"

#include "engine/input.h"
#include "fcse_api.h"

#include <cstdio>
#include <windows.h>

namespace {
    // Kept in step: one label per key, same order, so a Choice index is an index into both.
    const char* const kLabels[] = {
        "Off",       "F1",        "F2",        "F3",        "F4",        "F5",       "F6",
        "F7",        "F8",        "F9",        "F10",       "F11",       "F12",      "Insert",
        "Delete",    "End",       "Page up",   "Page down", "Pause",     "Scroll lock",
        "Numpad 0",  "Numpad 1",  "Numpad 2",  "Numpad 3",  "Numpad 4",  "Numpad 5", "Numpad 6",
        "Numpad 7",  "Numpad 8",  "Numpad 9",
    };

    constexpr int kVirtualKeys[] = {
        0,          VK_F1,      VK_F2,      VK_F3,      VK_F4,       VK_F5,      VK_F6,
        VK_F7,      VK_F8,      VK_F9,      VK_F10,     VK_F11,      VK_F12,     VK_INSERT,
        VK_DELETE,  VK_END,     VK_PRIOR,   VK_NEXT,    VK_PAUSE,    VK_SCROLL,
        VK_NUMPAD0, VK_NUMPAD1, VK_NUMPAD2, VK_NUMPAD3, VK_NUMPAD4,  VK_NUMPAD5, VK_NUMPAD6,
        VK_NUMPAD7, VK_NUMPAD8, VK_NUMPAD9,
    };

    constexpr size_t kKeyCount = sizeof(kLabels) / sizeof(kLabels[0]);
    static_assert(sizeof(kVirtualKeys) / sizeof(kVirtualKeys[0]) == kKeyCount);

    // The named indices in keys.h are a default binding away from being wrong if this list is
    // reordered, so they are checked against it here rather than trusted.
    static_assert(kVirtualKeys[DevTools::Keys::kChoiceOff] == 0);
    static_assert(kVirtualKeys[DevTools::Keys::kChoiceF1] == VK_F1);
    static_assert(kVirtualKeys[DevTools::Keys::kChoiceF2] == VK_F2);
}

namespace DevTools::Keys {

bool Focused() {
    if (Input::IsCaptured()) {
        return false;
    }

    static const DWORD self = GetCurrentProcessId();

    DWORD process = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &process);
    return process == self;
}

bool Down(int key) { return key != 0 && (GetAsyncKeyState(key) & 0x8000) != 0; }

bool Pressed(int key, bool& latch) {
    const bool down = Down(key);
    const bool edge = down && !latch;

    latch = down;
    return edge;
}

const char* const* ChoiceLabels(size_t& count) {
    count = kKeyCount;
    return kLabels;
}

int FromChoice(size_t choice) { return choice < kKeyCount ? kVirtualKeys[choice] : 0; }

void LogBinding(const char* name, size_t choice, int bound) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    char line[96];
    if (bound != 0) {
        std::snprintf(line, sizeof(line), "%s: bound to %s", name, kLabels[choice]);
    } else if (FromChoice(choice) != 0) {
        std::snprintf(line, sizeof(line), "%s: %s is already taken - left unbound", name,
                      kLabels[choice]);
    } else {
        std::snprintf(line, sizeof(line), "%s: off", name);
    }
    api->Log(line);
}

}
