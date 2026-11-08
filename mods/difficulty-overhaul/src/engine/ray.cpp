#include "engine/ray.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    using DifficultyOverhaul::Ray::Hit;

    // The hits a cast hands back: the hits, how many, and whether the storage is its own.
    struct Hits {
        const Hit* data;
        uint32_t size;
        uint32_t capacity;
    };
    constexpr uint32_t kBorrowed = 1;

    // CPhysQueryFilter: its vtable, the layers it lets hit, and a system group to skip.
    struct Filter {
        void* vtable;
        uint32_t layers;
        uint32_t group;
    };
    // The ground and solid things, no bodies; and every layer a bullet hits, bodies and vehicles with them.
    constexpr uint32_t kSightLayers = 0x33;
    constexpr uint32_t kBulletLayers = 0x5BB;
    // Nothing cast outside the world, and with it the closest hit only.
    constexpr uint32_t kInWorld = 4;
    constexpr uint32_t kClosest = 1 | kInWorld;

    // Base.Flesh and Base.Flesh_Head, by their ids in logicmaterials.xml.
    constexpr uint8_t kBody = 10;
    constexpr uint8_t kHead = 37;
    // The kind a physics body's third virtual names the terrain by.
    constexpr int kTerrain = 5;

    // CPhysicsSystem::CastRay(from, delta, hits, filter, ignored, flags), __thiscall on the system;
    // the filter's constructor (layers, group); and CPhysWorld::GetPhysEntityById on the same system.
    using CastRayFn = void(__fastcall*)(void* system, void* unused, const float* from, const float* delta,
                                        Hits* hits, const Filter* filter, void* ignored, uint32_t flags);
    using FilterFn = Filter*(__fastcall*)(Filter* filter, void* unused, uint32_t layers, uint32_t group);
    using GetBodyFn = uint8_t*(__fastcall*)(void* system, void* unused, uint32_t id);
    using KindFn = int(__fastcall*)(uint8_t* body, void* unused);
    FCSE::Relocation<CastRayFn> g_castRay{FCSE::Uplay(0x0048DD30)};
    FCSE::Relocation<FilterFn> g_makeFilter{FCSE::Uplay(0x0048EC40)};
    FCSE::Relocation<GetBodyFn> g_getBody{FCSE::Uplay(0x0048E790)};
    FCSE::Relocation<void**> g_system{FCSE::Uplay(0x01613D60)};

    bool g_installed = false;
    Filter g_sightFilter = {};
    Filter g_bulletFilter = {};

    void* System() {
        return g_installed ? *g_system.get() : nullptr;
    }

    // Casts the ray and hands each hit to `visit`; how many there were.
    template <class Visit>
    uint32_t Cast(const Filter& filter, uint32_t flags, const float from[3], const float direction[3], float length,
                  Visit visit) {
        void* system = System();
        if (system == nullptr) {
            return 0;
        }
        const float delta[3] = {direction[0] * length, direction[1] * length, direction[2] * length};
        Hits hits = {};
        g_castRay(system, nullptr, from, delta, &hits, &filter, nullptr, flags);
        for (uint32_t i = 0; i < hits.size; ++i) {
            visit(hits.data[i]);
        }
        if (hits.data != nullptr && (hits.capacity & kBorrowed) == 0) {
            DifficultyOverhaul::EngineFree(hits.data);
        }
        return hits.size;
    }
}

namespace DifficultyOverhaul::Ray {

bool Install() {
    g_installed = g_castRay && g_makeFilter && g_getBody && g_system && EngineFree;
    if (!g_installed) {
        FCSE::Logf("ray: the physics ray cast was not found in this build");
        return false;
    }
    g_makeFilter(&g_sightFilter, nullptr, kSightLayers, 0);
    g_makeFilter(&g_bulletFilter, nullptr, kBulletLayers, 0);
    return true;
}

bool Clear(const float from[3], const float direction[3], float length) {
    return Cast(g_sightFilter, kClosest, from, direction, length, [](const Hit&) {}) == 0;
}

bool FirstSurface(const float from[3], const float direction[3], float length, Hit& hit) {
    bool found = false;
    float distance = length;
    Cast(g_bulletFilter, kInWorld, from, direction, length, [&](const Hit& candidate) {
        if (!OnBody(candidate) && candidate.distance <= distance) {
            found = true;
            distance = candidate.distance;
            hit = candidate;
        }
    });
    return found;
}

bool OnBody(const Hit& hit) { return hit.material == kBody || hit.material == kHead; }

uint8_t* Body(const Hit& hit) {
    void* system = System();
    return system != nullptr ? g_getBody(system, nullptr, hit.body) : nullptr;
}

bool IsTerrain(uint8_t* body) {
    return (*reinterpret_cast<KindFn* const*>(body))[2](body, nullptr) == kTerrain;
}

}
