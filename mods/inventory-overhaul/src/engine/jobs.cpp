// CFCXPauseMenuPage::MissionObjective and SetupMissionObjective as code, at Steam's addresses: every
// game element's CMapElementComponent that is on and has a state. See
// docs/docs/engine-internals/campaign.md.
#include "engine/jobs.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "engine/story.h"
#include "fcse_api.h"

#include <cstring>
#include <utility>

namespace InventoryOverhaul::Jobs {

namespace {
    // GenericEntityMapIterator over the entity system's game elements: the map's node, the entity's
    // node in it, and the map.
    struct Iterator {
        const uint8_t* outer;
        const uint8_t* inner;
        const uint8_t* map;
    };
    using StepFn = void(__fastcall*)(Iterator* iterator);
    using LockFn = void(__fastcall*)(void* entity);
    using BuildClassIdFn = void(__fastcall*)(void* unused, void* unused2);
    using UndergroundFn = bool(__cdecl*)(const EngineString<char>* state);

    FCSE::Relocation<const uint8_t* const*> g_entities{FCSE::Uplay(0x01644E80)};
    FCSE::Relocation<StepFn> g_first{FCSE::Uplay(0x0070DDB0)};
    FCSE::Relocation<StepFn> g_next{FCSE::Uplay(0x0070E0D0)};
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    // CMapElementComponent's class id, built when its name is still unset.
    FCSE::Relocation<BuildClassIdFn> g_buildMarkerType{FCSE::Uplay(0x00041E60)};
    FCSE::Relocation<const void* const*> g_markerTypeName{FCSE::Uplay(0x00FDE178)};
    FCSE::Relocation<const uint32_t*> g_markerType{FCSE::Uplay(0x00FDE188)};
    FCSE::Relocation<UndergroundFn> g_isUnderground{FCSE::Uplay(0x00722160)};

    // A marker's journal line's key, its objective's key and whether it is on.
    constexpr ptrdiff_t kMarkerState = 0x128;
    constexpr ptrdiff_t kMarkerObjective = 0x144;
    constexpr ptrdiff_t kMarkerOn = 0x16C;
    // The entity system's game element map and its head node, an iterator node's entity proxy, and
    // the proxy's entity.
    constexpr ptrdiff_t kEntitiesMap = 0x1C;
    constexpr ptrdiff_t kMapHead = 0x10;
    constexpr ptrdiff_t kNodeProxy = 0x10;
    constexpr ptrdiff_t kProxyEntity = 0x0C;

    bool g_installed = false;

    std::wstring Words(const EngineString<char>* key) {
        return Story::Words("Objectives", std::string(key->Text(), key->length));
    }
}

bool Install() {
    g_installed = g_entities && g_first && g_next && g_lock && g_buildMarkerType && g_markerTypeName &&
                  g_markerType && g_isUnderground;
    return g_installed;
}

Current Find() {
    Current current;
    const uint8_t* entities = g_installed ? *g_entities.get() : nullptr;
    if (entities == nullptr) {
        return current;
    }
    if (*g_markerTypeName.get() == nullptr) {
        g_buildMarkerType(nullptr, nullptr);
    }
    const EngineString<char>* objectives[2] = {};
    size_t found = 0;
    const EngineString<char>* underground = nullptr;
    const uint8_t* map = entities + kEntitiesMap;
    const uint8_t* head = Read<const uint8_t*>(map, kMapHead);
    Iterator it{Read<const uint8_t*>(head, 0), nullptr, map};
    for (g_first(&it); it.outer != head; g_next(&it)) {
        void* entity = Read<void*>(Read<const void*>(it.inner, kNodeProxy), kProxyEntity);
        g_lock(entity);
        const auto* marker = static_cast<const uint8_t*>(Inventory::Component(entity, g_markerType.get()));
        if (marker == nullptr || Read<uint32_t>(marker, kMarkerOn) == 0) {
            continue;
        }
        const auto* state = reinterpret_cast<const EngineString<char>*>(marker + kMarkerState);
        const auto* objective = reinterpret_cast<const EngineString<char>*>(marker + kMarkerObjective);
        if (state->length == 0) {
            continue;
        }
        if (g_isUnderground(state)) {
            if (underground == nullptr) {
                underground = objective;
            }
            continue;
        }
        if (current.job.empty()) {
            current.job = Words(state);
        }
        if (objective->length > 0 && found < 2) {
            objectives[found++] = objective;
        }
    }
    // Of two objectives, the one whose key ends in _01 comes first.
    if (found == 2) {
        const char* last = std::strrchr(objectives[0]->Text(), '_');
        if (last == nullptr || std::strcmp(last, "_01") != 0) {
            std::swap(objectives[0], objectives[1]);
        }
    }
    for (size_t i = 0; i < found; ++i) {
        current.objectives[i] = Words(objectives[i]);
    }
    if (underground != nullptr && underground->length > 0) {
        current.underground = Words(underground);
    }
    return current;
}

}
