// Borderless window.
//
// Far Cry 2 offers fullscreen and windowed and nothing between them, so alt-tabbing means either a
// mode switch or a title bar. The engine itself already knows how: `-borderless` is one of its own
// command-line flags, and it strips WS_CAPTION for a plain WS_POPUP while keeping the client size
// the player chose.
//
// FC2JackalFix implements this the other way, rewriting the window style and the present parameters
// from inside the plugin (source/display/borderless.ixx). That buys switching mode without
// restarting, at the cost of nearly six hundred lines that have to keep out of the way of every
// device reset. Asking the engine for the flag it already understands is the same result on the
// path the engine supports, and it costs the setting nothing except that it applies on next launch.
//
// Exclusive fullscreen and plain windowed are already on the game's own video options page, so this
// row offers only what is missing from it.
#include "fcse_api.h"

#include "engine/command_line.h"

namespace {
    // Index into the labels declared in main.cpp; 0 is the game's own.
    constexpr uint32_t kBorderless = 1;
}

void __cdecl OnDisplayModeChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const bool borderless = value->asChoice == kBorderless;

    // An empty value rather than null: the flag takes no argument, and null would withdraw it.
    UFCP::SetCommandLineSwitch("borderless", borderless ? "" : nullptr);

    api->Log(borderless ? "display mode: borderless, from the next launch"
                        : "display mode: the game's own");
}
