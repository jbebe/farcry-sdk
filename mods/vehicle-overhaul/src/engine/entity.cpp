// Entities, reached from one of their components.
#include "engine/entity.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    // A component's entity holder, the entity in it, and the entity's transform.
    constexpr ptrdiff_t kComponentRef = 0x08;
    constexpr ptrdiff_t kRefEntity = 0x0C;
    constexpr ptrdiff_t kEntityMatrix = 0x30;

    using FlushJobFn = void(__fastcall*)(void* entity);
    using Vec3Fn = void(__fastcall*)(void* entity, void* unused, float a, float b, float c);

    FCSE::Relocation<FlushJobFn> g_flushJob{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<Vec3Fn> g_setPosition{FCSE::Uplay(0x004E01B0)};
    FCSE::Relocation<Vec3Fn> g_setEuler{FCSE::Uplay(0x004E01C0)};
}

namespace VehicleOverhaul::Entity {

bool Install() {
    if (!g_flushJob || !g_setPosition || !g_setEuler) {
        FCSE::ApiPointer()->Log("entity: the calls that move an entity were not found in this build");
        return false;
    }
    return true;
}

void* Of(void* component) {
    if (component == nullptr) {
        return nullptr;
    }
    auto* ref = *reinterpret_cast<uint8_t**>(static_cast<uint8_t*>(component) + kComponentRef);
    return ref != nullptr ? *reinterpret_cast<void**>(ref + kRefEntity) : nullptr;
}

const float* Matrix(void* entity) {
    return reinterpret_cast<const float*>(static_cast<uint8_t*>(entity) + kEntityMatrix);
}

void Place(void* entity, const float* position, const float* angles) {
    g_flushJob(entity);
    g_setPosition(entity, nullptr, position[0], position[1], position[2]);
    g_setEuler(entity, nullptr, angles[0], angles[1], angles[2]);
}

}
