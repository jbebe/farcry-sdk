// The game's material impact records, and decals spawned from them where a ray meets the world.
#include "engine/impacts.h"

#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

namespace {
    using GetRecordFn = const void*(__cdecl*)(uint32_t name);
    using ExecuteSecondaryFn = void(__fastcall*)(const void* record, void* unused, uint32_t material,
                                                 const void* collision);
    using PlaceableFn = char(__stdcall*)(void* physics, const float* from, const float* probe, const float* corners);

    // CMaterialImpactFx::GetMaterialImpact.
    FCSE::Relocation<GetRecordFn> g_getRecord{FCSE::Uplay(0x005111F0)};
    // CMaterialImpactFx::ExecuteSecondary: a record's particles and decal for the surface hit, with no sound.
    FCSE::Relocation<ExecuteSecondaryFn> g_executeSecondary{FCSE::Uplay(0x00511A90)};
    // CDecalManager::IsDecalPlaceable: whether all four corners of a decal touch the object it goes on.
    FCSE::Relocation<PlaceableFn> g_placeable{FCSE::Uplay(0x00527370)};
    PlaceableFn g_originalPlaceable = nullptr;

    // CMaterialImpactFx::SCollisionInfo, filled from a ray hit as CWeaponFireBulletProperties::SpawnBulletPuff
    // fills it: a decal goes on the terrain, on the entity hit, or on the static geometry hit.
    struct Collision {
        float point[3];
        float normal[3];
        float up[3];
        uint32_t unused;
        uint64_t entity;
        uint32_t part;
        uint32_t tree;
        uint32_t treePart;
        bool terrain;
    };
    static_assert(sizeof(Collision) == 0x40);
    constexpr uint32_t kNone = 0xFFFFFFFF;

    bool g_installed = false;
    // Whether the decal being spawned now is one of ours, which an object shows only where it reaches.
    thread_local bool t_spawning = false;

    char __stdcall PlaceableDetour(void* physics, const float* from, const float* probe, const float* corners) {
        return t_spawning || g_originalPlaceable(physics, from, probe, corners);
    }
}

namespace DifficultyOverhaul::Impacts {

bool Install() {
    static const bool installed = [] {
        g_installed = g_getRecord && g_executeSecondary && Ray::Install() &&
                      Hook(g_placeable, &PlaceableDetour, &g_originalPlaceable);
        if (!g_installed) {
            FCSE::Logf("impacts: the impact records or their decals were not found in this build");
        }
        return g_installed;
    }();
    return installed;
}

const void* Record(uint32_t name) { return g_installed ? g_getRecord(name) : nullptr; }

void SpawnDecal(const void* record, const Ray::Hit& hit) {
    if (record == nullptr) {
        return;
    }
    Collision collision = {{hit.point[0], hit.point[1], hit.point[2]},
                           {hit.normal[0], hit.normal[1], hit.normal[2]},
                           {0.0f, 1.0f, 0.0f},
                           0,
                           ~0ull,
                           kNone,
                           hit.tree,
                           hit.treePart,
                           false};
    if (uint8_t* body = Ray::Body(hit)) {
        if (const void* owner = Game::OwnerOf(body)) {
            collision.entity = Game::FullId(owner);
            collision.part = hit.part;
        }
        collision.terrain = Ray::IsTerrain(body);
    }
    t_spawning = true;
    g_executeSecondary(record, nullptr, hit.material, &collision);
    t_spawning = false;
}

}
