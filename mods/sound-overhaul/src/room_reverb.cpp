// Room reverb: every building's reverb follows its structure type and size, set as the building loads,
// before its reverb event is registered to load. Retail picked one of four by hand, so a bus rang like a
// shanty for 2.5 s and 84 buildings kept the outdoor reverb.
#include "fcse_api.h"

#include <algorithm>
#include <cstdint>

namespace {
    // CBuildingInfoComponent::ResizeBoundingBox (ECX = the component): sizes the building's box from
    // vectorSize, then registers its SoundParams' resources.
    FCSE::Relocation<uint8_t*> g_resizeBoundingBox{FCSE::Pattern(
        "83 EC 18 F3 0F 10 1D ?? ?? ?? ?? 53 56 8B F1 F3 0F 10 46 10 F3 0F 10 4E 14 F3 0F 10 56 18 "
        "F3 0F 59 C3 F3 0F 59 D3")};

    constexpr uintptr_t kSize = 0x10;
    constexpr uintptr_t kStructureType = 0x20;
    // SoundParams at +0x64, its sndReverb 8 bytes in.
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

    void OnResizeBoundingBox(FCSE_MidHookContext* ctx) {
        const auto* size = reinterpret_cast<const float*>(ctx->ecx + kSize);
        const uint32_t type = *reinterpret_cast<const uint32_t*>(ctx->ecx + kStructureType);
        *reinterpret_cast<uint32_t*>(ctx->ecx + kReverb) = ReverbFor(type, (std::max)(size[0], size[1]));
    }
}

void ApplyRoomReverb() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_resizeBoundingBox) {
        api->Log("room reverb: CBuildingInfoComponent::ResizeBoundingBox was not found in this build");
        return;
    }

    if (api->MidHook(g_resizeBoundingBox.get(), &OnResizeBoundingBox)) {
        api->Log("room reverb: buildings take their reverb from their type and size");
    }
}
