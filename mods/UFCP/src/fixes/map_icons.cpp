// The map's icons shift slightly on the paper, by less than a pixel, as the player moves.
//
// The engine draws what the player holds at a precise position. The player's binding is made
// precise at spawn, so each thing bound to the player, the map among them, gets a world matrix and,
// beside it, the part float precision dropped (CBindingComponent::UpdateChildMatrices, Steam
// 0x10553500, Retail 0x10545630). The graphic draws from the whole part and those fractions
// (0x1051B2A0, Retail 0x1050D300). A float carries about a quarter of a millimetre 2-4 km from the
// world's origin, where world 1 lies, and the map is half a metre from the eye, so the fractions are
// worth half a pixel.
//
// The icons are bound on the map's corner bone, one level further down, and the map's binding is
// not precise. Each icon's world matrix is the map's float matrix times the bone and its spot,
// worked in world-sized numbers, and the map's own fractions are never added, so an icon lands on
// the quarter-millimetre grid and jumps a step across the paper whenever the map moves through one.
// The map's binding also places its icons before the player's binding moves the map in each frame,
// so they are drawn where the map was the frame before.
//
// So a map's binding places its children through the engine's own precise path, made precise for
// that one call only since PrecisePosition is a property the engine saves, and adds the map's own
// fractions to each child's; and it does so again after the binding the map hangs from has moved
// the map. An icon draws where the paper under it does. UpdateMarkers (0x106F39F0, Retail
// 0x106E6360) only names the map. Nothing tells a graphic its fractions changed: its moved handler
// (0x1051A760, Retail 0x1050C770) clears them.
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>
#include <iterator>

namespace {
    // All __thiscall, which a free function spells __fastcall with an unused EDX.
    using UpdateMarkersFn = void(__fastcall*)(uint8_t* strategy, void* unused, float seconds, void* pawn,
                                              int sector);
    using UpdateChildrenFn = void(__fastcall*)(uint8_t* binding, void* unused, bool force);
    using LockFn = void(__fastcall*)(uint8_t* entity, void* unused);
    using GetComponentFn = uint8_t*(__fastcall*)(uint8_t* entity, void* unused, const uint32_t* id);

    FCSE::Relocation<uint8_t*> g_updateMarkersSite{FCSE::Uplay(0x006F39F0)};
    FCSE::Relocation<uint8_t*> g_updateChildrenSite{FCSE::Uplay(0x00553500)};
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};
    UpdateMarkersFn g_updateMarkers = nullptr;
    UpdateChildrenFn g_updateChildren = nullptr;

    constexpr ptrdiff_t kStrategyEquipment = 0x38;
    constexpr ptrdiff_t kHandleEntity = 0x0C;
    // CEntity and CBindingComponent.
    constexpr ptrdiff_t kEntityBinding = 0x78;
    constexpr ptrdiff_t kParentHandle = 0x18;
    constexpr ptrdiff_t kChildren = 0x2C;
    constexpr ptrdiff_t kChildCount = 0x30;
    // A child placed by its own matrix, which the engine leaves alone; the binding's PrecisePosition.
    constexpr ptrdiff_t kOwnMatrix = 0xC4;
    constexpr ptrdiff_t kPrecise = 0xC5;
    // CGraphicComponent.
    constexpr ptrdiff_t kGraphicFlags = 0x3D;
    constexpr uint8_t kHasFractions = 0x04;
    constexpr ptrdiff_t kFractions = 0x1B0;
    constexpr uint32_t kGraphicComponent = FCSE::Crc32("CGraphicComponent");

    // A map's binding updates twice a frame; it is placed precisely for this many updates after its
    // markers were last placed, so a map put away is left to the engine.
    constexpr int kUpdates = 3;

    // Game thread only, so no atomics. The maps placed most recently, the hand's and a vehicle's,
    // each with the binding it hangs from.
    struct Map {
        uint8_t* binding = nullptr;
        uint8_t* parent = nullptr;
        int updates = 0;
    };
    Map g_maps[2];
    size_t g_nextMap = 0;
    bool g_reported = false;

    template <class T>
    T& Field(void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<uint8_t*>(object) + offset);
    }

    uint8_t* OwnerOf(void* component) {
        if (component == nullptr) {
            return nullptr;
        }
        return static_cast<uint8_t*>(FCSE::ApiPointer()->EntityData->EntityOf(component));
    }

    uint8_t* EntityOf(uint8_t* handle) {
        return handle != nullptr ? Field<uint8_t*>(handle, kHandleEntity) : nullptr;
    }

    uint8_t* BindingOfEntity(uint8_t* entity) {
        return entity != nullptr ? Field<uint8_t*>(entity, kEntityBinding) : nullptr;
    }

    uint8_t* GraphicOf(uint8_t* entity) {
        g_lock(entity, nullptr);
        return g_getComponent(entity, nullptr, &kGraphicComponent);
    }

    // Places the map's children through the engine's precise path, then adds the map's own fractions.
    void PlacePrecisely(uint8_t* map, uint8_t* binding) {
        uint8_t& precise = Field<uint8_t>(binding, kPrecise);
        const uint8_t wasPrecise = precise;
        precise = 1;
        g_updateChildren(binding, nullptr, true);
        precise = wasPrecise;

        uint8_t* graphic = GraphicOf(map);
        const bool hasFractions =
            graphic != nullptr && (Field<uint8_t>(graphic, kGraphicFlags) & kHasFractions) != 0;
        if (!g_reported) {
            g_reported = true;
            FCSE::Logf("map icons: placed on a map drawn at its %s",
                       hasFractions ? "precise position" : "float position");
        }
        if (!hasFractions) {
            return;
        }

        const float* from = &Field<float>(graphic, kFractions);
        uint8_t** children = Field<uint8_t**>(binding, kChildren);
        const uint32_t count = Field<uint32_t>(binding, kChildCount);
        for (uint32_t i = 0; i < count; ++i) {
            uint8_t* child = EntityOf(children[i]);
            uint8_t* childBinding = BindingOfEntity(child);
            if (childBinding == nullptr || Field<uint8_t>(childBinding, kOwnMatrix) != 0) {
                continue;
            }
            uint8_t* childGraphic = GraphicOf(child);
            // The engine's precise path just gave every child with a graphic its own fractions.
            if (childGraphic == nullptr ||
                (Field<uint8_t>(childGraphic, kGraphicFlags) & kHasFractions) == 0) {
                continue;
            }
            float* to = &Field<float>(childGraphic, kFractions);
            for (int k = 0; k < 3; ++k) {
                to[k] += from[k];
            }
        }
    }

    void __fastcall OnUpdateChildren(uint8_t* binding, void* unused, bool force) {
        for (Map& map : g_maps) {
            if (map.binding == binding && map.updates > 0) {
                --map.updates;
                return PlacePrecisely(OwnerOf(binding), binding);
            }
        }
        g_updateChildren(binding, unused, force);
        for (Map& map : g_maps) {
            if (map.parent == binding && map.updates > 0) {
                PlacePrecisely(OwnerOf(map.binding), map.binding);
            }
        }
    }

    void __fastcall OnUpdateMarkers(uint8_t* strategy, void* unused, float seconds, void* pawn,
                                    int sector) {
        g_updateMarkers(strategy, unused, seconds, pawn, sector);
        uint8_t* binding = BindingOfEntity(OwnerOf(Field<uint8_t*>(strategy, kStrategyEquipment)));
        if (binding == nullptr) {
            return;
        }
        Map* map = nullptr;
        for (Map& known : g_maps) {
            if (known.binding == binding) {
                map = &known;
            }
        }
        if (map == nullptr) {
            map = &g_maps[g_nextMap++ % std::size(g_maps)];
            map->binding = binding;
        }
        map->parent = BindingOfEntity(EntityOf(Field<uint8_t*>(binding, kParentHandle)));
        map->updates = kUpdates;
    }
}

void ApplyMapIconsFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    const bool found = g_lock && g_getComponent && g_updateChildrenSite && g_updateMarkersSite;
    const bool hooked =
        found &&
        api->Hook(g_updateChildrenSite.get(), reinterpret_cast<void*>(&OnUpdateChildren),
                  reinterpret_cast<void**>(&g_updateChildren)) &&
        api->Hook(g_updateMarkersSite.get(), reinterpret_cast<void*>(&OnUpdateMarkers),
                  reinterpret_cast<void**>(&g_updateMarkers));
    FCSE::Logf("map icons: %s",
               hooked ? "the map's icons placed at the precise position the map is drawn at"
                      : "the binding update or the map's marker update was not found - not fixed");
}
