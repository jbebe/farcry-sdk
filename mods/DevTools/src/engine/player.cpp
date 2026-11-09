// The local player, and what hangs off it.
#include "engine/player.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>

namespace {
    using GetLocalPlayerFn = void*(__cdecl*)();
    using PawnClassFn = const uint8_t*(__cdecl*)();
    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);

    FCSE::Relocation<GetLocalPlayerFn> g_getLocalPlayerSite{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<GetCurrentVehicleFn> g_getCurrentVehicleSite{FCSE::Uplay(0x000E7330)};

    // CPawn's class hierarchy, built on first use, and where CPawn's own class id sits in it.
    FCSE::Relocation<PawnClassFn> g_pawnClass{FCSE::Uplay(0x00040A80)};
    constexpr ptrdiff_t kHierarchyPawnId = 0x14;

    // Resolved once: an unresolved Relocation re-scans on every access, and this is on the frame.
    GetLocalPlayerFn g_getLocalPlayer = nullptr;
    GetCurrentVehicleFn g_getCurrentVehicle = nullptr;

    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;

    // The pawn's data, in it the desired and the effective look, and in each the pitch, up positive,
    // and the yaw, left positive, in radians and each kept twice; then the effective stance.
    // See docs/docs/engine-internals/first-person-aiming.md#the-look.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kLooks[] = {0x140, 0x2D0};
    constexpr ptrdiff_t kLookPitches[] = {0x38, 0x44};
    constexpr ptrdiff_t kLookYaws[] = {0x40, 0x4C};
    constexpr ptrdiff_t kEffectiveStance = 0x374;
    constexpr int32_t kCrouched = 1;
}

namespace DevTools::Player {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (g_getLocalPlayerSite) {
        g_getLocalPlayer = g_getLocalPlayerSite.get();
    } else {
        api->Log("player: the local player accessor was not found in this build");
    }
    if (g_getCurrentVehicleSite) {
        g_getCurrentVehicle = g_getCurrentVehicleSite.get();
    } else {
        api->Log("player: CVehicle::GetCurrentVehicle was not found in this build");
    }
    if (!g_pawnClass) {
        api->Log("player: the pawn's class was not found in this build");
    }
}

void* Local() { return g_getLocalPlayer != nullptr ? g_getLocalPlayer() : nullptr; }

void* Pawn() {
    void* local = Local();
    if (local == nullptr || !g_pawnClass) {
        return nullptr;
    }

    // The scene is a component of the player's entity, which also holds the pawn.
    void* entity = FCSE::ApiPointer()->EntityData->EntityOf(Read<void*>(local, kPlayerScene));
    return entity != nullptr ? Entity::Component(entity, g_pawnClass() + kHierarchyPawnId) : nullptr;
}

void* Vehicle(void* pawn) {
    return pawn != nullptr && g_getCurrentVehicle != nullptr ? g_getCurrentVehicle(pawn) : nullptr;
}

namespace {
    uint8_t* Data() {
        void* pawn = Pawn();
        return pawn != nullptr ? Read<uint8_t*>(pawn, kPawnData) : nullptr;
    }
}

bool FaceLook(std::optional<float> yawDegrees, std::optional<float> pitchDegrees) {
    uint8_t* data = Data();
    if (data == nullptr) {
        return false;
    }

    // The look's yaw is turned by its difference to the body's, which it is not measured against.
    float turn = 0.0f;
    if (yawDegrees) {
        void* ref = Entity::AcquireFocusRef(CameraManager());
        if (void* entity = Entity::Of(ref)) {
            turn = std::remainder(*yawDegrees - Entity::Degrees(Entity::Yaw(entity)), 360.0f) *
                   Entity::kRadiansPerDegree;
        }
        Entity::ReleaseRef(ref);
    }

    for (const ptrdiff_t look : kLooks) {
        for (int copy = 0; copy < 2; ++copy) {
            *reinterpret_cast<float*>(data + look + kLookYaws[copy]) += turn;
            if (pitchDegrees) {
                *reinterpret_cast<float*>(data + look + kLookPitches[copy]) = *pitchDegrees * Entity::kRadiansPerDegree;
            }
        }
    }
    return true;
}

float LookPitch() {
    uint8_t* data = Data();
    return data != nullptr ? Read<float>(data, kLooks[1] + kLookPitches[0]) : 0.0f;
}

bool Crouched() {
    uint8_t* data = Data();
    return data != nullptr && Read<int32_t>(data, kEffectiveStance) == kCrouched;
}

void* CameraManager(void* local) {
    if (local == nullptr) {
        return nullptr;
    }

    // The manager is embedded in the scene rather than pointed at, so this is its address.
    auto* scene = *reinterpret_cast<uint8_t**>(static_cast<uint8_t*>(local) + kPlayerScene);
    return scene != nullptr ? scene + kSceneCameraManager : nullptr;
}

void* CameraManager() { return CameraManager(Local()); }

bool ObjectIsLive(void* object, void* vtable, void* local) {
    if (object == nullptr || vtable == nullptr || local == nullptr) {
        return false;
    }
    return *reinterpret_cast<void**>(object) == vtable;
}

}
