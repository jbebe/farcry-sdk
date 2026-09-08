// Noclip.
//
// Far Cry 2 has no noclip and no console command that reaches one. This detaches the player
// directly: the character controller's physics are switched off, which removes collision and
// gravity, and the body is then flown by writing its position each frame. The view stays on the
// head, so it is still the player's own camera and the HUD is still up - which is what makes it
// useful for walking a level rather than photographing one.
//
// The pause menu, quicksave and quickload are refused while it is up: saving a position the player
// could not have reached, or pausing into a menu that puts them back on the ground, both end badly.
//
// The mechanism, the bindings and the mutual exclusion with the free camera are in
// engine/debug_camera; what is here is which key it answers to.
#include "engine/debug_camera.h"
#include "engine/keys.h"

namespace {
    int g_choice = 0;
}

int GetNoclipKey() { return g_choice; }

void SetNoclipKey(int value) {
    if (value == g_choice) {
        return;
    }

    const int bound = DevTools::DebugCamera::Bind(DevTools::DebugCamera::Mode::Noclip,
                                                  DevTools::Keys::FromChoice(value));

    // A key the free camera already holds is refused, and the row goes back to Off rather than
    // showing a binding that does nothing.
    g_choice = (bound != 0) ? value : 0;
    DevTools::Keys::LogBinding("noclip", value, bound);
}
