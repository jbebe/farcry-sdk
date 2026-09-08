// Free camera.
//
// The shipped game still carries a free camera, and still binds F3 and F4 to it in the input maps,
// but the handlers those bindings name are gone from the retail binary and no console command
// reaches one - which is why docs/docs/engine-internals/developer-console.md long recorded free-fly
// as editor-only. What is missing is only the way in: the camera manager will still activate the
// prototype by name, and this asks it to.
//
// Unlike noclip the player stays where they are and keeps their body; only the view detaches, which
// is what makes it the right one for looking at the player, at a firefight, or at anything that has
// to keep happening while you watch.
//
// The archetype is entity-library data rather than a string in Dunia.dll, so a data set can simply
// not carry it; the log says so when that happens. The mechanism and the mutual exclusion with
// noclip are in engine/debug_camera; what is here is which key it answers to.
#include "engine/debug_camera.h"
#include "engine/keys.h"

namespace {
    int g_choice = 0;
}

int GetFreecamKey() { return g_choice; }

void SetFreecamKey(int value) {
    if (value == g_choice) {
        return;
    }

    const int bound = DevTools::DebugCamera::Bind(DevTools::DebugCamera::Mode::Freecam,
                                                  DevTools::Keys::FromChoice(value));

    // A key noclip already holds is refused, and the row goes back to Off rather than showing a
    // binding that does nothing.
    g_choice = (bound != 0) ? value : 0;
    DevTools::Keys::LogBinding("freecam", value, bound);
}
