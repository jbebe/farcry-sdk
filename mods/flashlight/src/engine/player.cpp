// The local player.
#include "engine/player.h"

#include "fcse_api.h"

namespace {
    using LocalPlayerFn = void*(__cdecl*)();

    FCSE::Relocation<LocalPlayerFn> g_localPlayer{FCSE::Uplay(0x00831870)};
}

namespace Flashlight::Player {

bool Install() {
    if (!g_localPlayer) {
        FCSE::ApiPointer()->Log("player: the local player accessor was not found in this build");
        return false;
    }
    return true;
}

void* Local() { return g_localPlayer(); }

}
