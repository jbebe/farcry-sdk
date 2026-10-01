// Rooms: every building's reverb follows its structure type and size, and its walls muffle sound from outside by
// their material, set as the building loads, before its reverb event is registered to load. Retail picked one of
// four reverbs by hand, so a bus rang like a shanty for 2.5 s, and 472 buildings muffled nothing. Open doors and
// windows reach into the room by their size, so a house with its windows open is barely muffled inside.
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
    // CBuildingInfoComponent::ResizeBoundingBox (ECX = the component): sizes the building's box from
    // vectorSize, then registers its SoundParams' resources.
    FCSE::Relocation<uint8_t*> g_resizeBoundingBox{FCSE::Pattern(
        "83 EC 18 F3 0F 10 1D ?? ?? ?? ?? 53 56 8B F1 F3 0F 10 46 10 F3 0F 10 4E 14 F3 0F 10 56 18 "
        "F3 0F 59 C3 F3 0F 59 D3")};

    // CSoundOcclusionManager::AddHole (cdecl): registers a door or window, whose occlusion fades toward its own
    // within its range, by squared distance. Its arguments, from ESP on entry: half-size at +0x18, filter at
    // +0x2C, range at +0x30.
    FCSE::Relocation<uint8_t*> g_addHole{FCSE::Pattern(
        "56 8B 74 24 0C 8B 06 85 C0 0F 84 ?? ?? ?? ?? F3 0F 10 50 1C F3 0F 10 48 20 F3 0F 10 40 18 "
        "F3 0F 58 44 24 34")};
    constexpr uintptr_t kHoleHalfSize = 0x18;
    constexpr uintptr_t kHoleFilter = 0x2C;
    constexpr uintptr_t kHoleRange = 0x30;

    constexpr uintptr_t kSize = 0x10;
    constexpr uintptr_t kStructureType = 0x20;
    // SoundParams at +0x64: fOcclusionFilter 4 bytes in, sndReverb 8.
    constexpr uintptr_t kFilter = 0x64 + 0x4;
    constexpr uintptr_t kReverb = 0x64 + 0x8;

    // selStructureType, in the order of its enum.
    enum StructureType : uint32_t {
        Generic, ShantyShack, StationaryBarge, PassengerRailCar, BoxCar, ShippingContainer, Warehouse, Hangar, Hut,
        NewUrban, OldColonial, MudHut, DungHut, StationaryBus,
    };

    // Reverb events: retail's small room, and the mod's own on presets retail authored but never used.
    constexpr uint32_t kSmallRoom = 0x004BE3E6;
    constexpr uint32_t kMediumRoom = 0x00FC0902;
    constexpr uint32_t kLargeRoom = 0x00FC0903;
    constexpr uint32_t kHall = 0x00FC0904;
    constexpr uint32_t kHangar = 0x00FC0905;
    constexpr uint32_t kMetalBox = 0x00FC0906;

    // Modest on purpose: whether a room is carpeted or bare is not in the data.
    uint32_t ReverbFor(uint32_t type, float longestSide) {
        switch (type) {
        case PassengerRailCar:
        case BoxCar:
        case ShippingContainer:
        case StationaryBus:
            return kMetalBox;
        case Hut:
        case MudHut:
        case DungHut:
            return kSmallRoom;
        case ShantyShack:
            return kMediumRoom;
        case Hangar:
            return kHangar;
        case Warehouse:
            return longestSide < 15.0f ? kLargeRoom : kHall;
        default:
            return longestSide < 8.0f    ? kSmallRoom
                   : longestSide < 15.0f ? kMediumRoom
                   : longestSide < 30.0f ? kLargeRoom
                                         : kHall;
        }
    }

    // The walls' filter, eyeballed by material. The low-pass on sound from outside falls steeply with it:
    // 0.001 cuts above about 1.6 kHz, 0.01 about 1.2 kHz, 0.05 about 840 Hz, 1.0 (retail's armory) 20 Hz.
    float FilterFor(uint32_t type) {
        switch (type) {
        case NewUrban:
        case OldColonial:
        case MudHut:
        case DungHut:
            return 0.05f;
        case Generic:
            return 0.03f;
        case Warehouse:
            return 0.02f;
        case BoxCar:
        case ShippingContainer:
        case Hangar:
            return 0.01f;
        case Hut:
            return 0.005f;
        case PassengerRailCar:
        case StationaryBarge:
            return 0.003f;
        case ShantyShack:
            return 0.002f;
        default:
            return 0.001f;
        }
    }

    void OnResizeBoundingBox(FCSE_MidHookContext* ctx) {
        const auto* size = reinterpret_cast<const float*>(ctx->ecx + kSize);
        const uint32_t type = *reinterpret_cast<const uint32_t*>(ctx->ecx + kStructureType);
        *reinterpret_cast<uint32_t*>(ctx->ecx + kReverb) = ReverbFor(type, (std::max)(size[0], size[1]));
        // Never below retail's: its strong walls, the armory's among them, were set by hand.
        auto* filter = reinterpret_cast<float*>(ctx->ecx + kFilter);
        *filter = (std::max)(*filter, FilterFor(type));
    }

    // An open door or window reaches 2 m plus 1.5 times the side of a square of its area, 4 m for a window,
    // at most 8 m; retail's 1-2 m left the room muffled a step away from it. Openings retail filters itself,
    // the barricaded ones among them, keep their range.
    void OnAddHole(FCSE_MidHookContext* ctx) {
        if (*reinterpret_cast<const float*>(ctx->esp + kHoleFilter) > 0.01f) {
            return;
        }
        float half[3];
        std::copy_n(reinterpret_cast<const float*>(ctx->esp + kHoleHalfSize), 3, half);
        std::sort(half, half + 3);
        const float area = 4.0f * half[1] * half[2];
        auto* range = reinterpret_cast<float*>(ctx->esp + kHoleRange);
        *range = (std::max)(*range, (std::min)(2.0f + 1.5f * std::sqrt(area), 8.0f));
    }
}

void ApplyRooms() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_resizeBoundingBox) {
        api->Log("rooms: CBuildingInfoComponent::ResizeBoundingBox was not found in this build");
        return;
    }

    if (api->MidHook(g_resizeBoundingBox.get(), &OnResizeBoundingBox)) {
        api->Log("rooms: buildings take their reverb from their type and size, and their muffle from their material");
    }

    if (!g_addHole) {
        api->Log("rooms: CSoundOcclusionManager::AddHole was not found in this build - doors and windows keep their reach");
    } else if (api->MidHook(g_addHole.get(), &OnAddHole)) {
        api->Log("rooms: open doors and windows reach into the room by their size");
    }
}
