// What each call does is in docs/docs/engine-internals/loading-a-save.md.
#include "engine/world.h"

#include "engine/entity.h"
#include "engine/player.h"
#include "fcse_api.h"

namespace {
    using FlagFn = bool(__cdecl*)();
    using TeleportFn = void(__cdecl*)(const float* position);

    FCSE::Relocation<FlagFn> g_loopEnabledSite{FCSE::Uplay(0x0032D710)};
    FCSE::Relocation<FlagFn> g_loadPendingSite{FCSE::Uplay(0x004DB900)};
    FCSE::Relocation<TeleportFn> g_teleportSite{FCSE::Uplay(0x00136B60)};

    FlagFn g_loopEnabled = nullptr;
    FlagFn g_loadPending = nullptr;
    TeleportFn g_teleport = nullptr;
}

namespace DevTools::World {

void Install() {
    if (!g_loopEnabledSite || !g_loadPendingSite || !g_teleportSite) {
        FCSE::ApiPointer()->Log("world: the loading state or the teleport was not found in this build");
        return;
    }
    g_loopEnabled = g_loopEnabledSite.get();
    g_loadPending = g_loadPendingSite.get();
    g_teleport = g_teleportSite.get();
}

bool LoadingScreen() { return g_loopEnabled != nullptr && g_loopEnabled(); }

bool Streaming() { return g_loadPending != nullptr && g_loadPending(); }

bool Teleport(float x, float y, float z, std::optional<float> yawDegrees) {
    if (g_teleport == nullptr || Player::Local() == nullptr) {
        return false;
    }
    const float position[3] = {x, y, z};
    g_teleport(position);

    if (yawDegrees) {
        void* ref = Entity::AcquireFocusRef(Player::CameraManager());
        if (void* entity = Entity::Of(ref)) {
            Entity::SetEuler(entity, 0.0f, 0.0f, *yawDegrees * Entity::kRadiansPerDegree);
        }
        Entity::ReleaseRef(ref);
    }
    return true;
}

}
