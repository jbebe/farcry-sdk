#include "engine/ray.h"

#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <iterator>

namespace {
    // The hits a cast hands back: the hits, how many, and whether the storage is its own.
    struct Hits {
        const float* data;
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
    // The layer masks, in the order of Ray::Layers.
    constexpr uint32_t kLayers[] = {0x5BB, 0x33};
    // The closest hit only, and nothing cast outside the world.
    constexpr uint32_t kClosest = 1 | 4;

    // CPhysicsSystem::CastRay(from, delta, hits, filter, ignored, flags), __thiscall on the system;
    // the filter's constructor (layers, group); and the engine's free.
    using CastRayFn = void(__fastcall*)(void* system, void* unused, const float* from,
                                        const float* delta, Hits* hits, const Filter* filter,
                                        void* ignored, uint32_t flags);
    using FilterFn = Filter*(__fastcall*)(Filter* filter, void* unused, uint32_t layers,
                                          uint32_t group);
    using FreeFn = void(__cdecl*)(const void* memory);
    FCSE::Relocation<CastRayFn> g_castRay{FCSE::Uplay(0x0048DD30)};
    FCSE::Relocation<FilterFn> g_makeFilter{FCSE::Uplay(0x0048EC40)};
    FCSE::Relocation<FreeFn> g_free{FCSE::Uplay(0x00228AD0)};
    FCSE::Relocation<void**> g_system{FCSE::Uplay(0x01613D60)};

    bool g_installed = false;
    Filter g_filters[std::size(kLayers)] = {};
}

bool ThirdPerson::Ray::Install() {
    g_installed = g_castRay && g_makeFilter && g_free && g_system;
    if (!g_installed) {
        FCSE::ApiPointer()->Log("ray: the physics ray cast was not found");
        return false;
    }
    for (size_t i = 0; i < std::size(kLayers); ++i) {
        g_makeFilter(&g_filters[i], nullptr, kLayers[i], 0);
    }
    return true;
}

float ThirdPerson::Ray::Cast(const float from[3], const float direction[3], float length, Layers layers) {
    void* system = g_installed ? *g_system.get() : nullptr;
    if (system == nullptr) {
        return length;
    }
    const float delta[3] = {direction[0] * length, direction[1] * length, direction[2] * length};
    Hits hits = {};
    g_castRay(system, nullptr, from, delta, &hits, &g_filters[static_cast<size_t>(layers)], nullptr,
              kClosest);
    float distance = length;
    if (hits.size > 0) {
        distance = std::hypot(hits.data[0] - from[0], hits.data[1] - from[1], hits.data[2] - from[2]);
    }
    if (hits.data != nullptr && (hits.capacity & kBorrowed) == 0) {
        g_free(hits.data);
    }
    return distance;
}
