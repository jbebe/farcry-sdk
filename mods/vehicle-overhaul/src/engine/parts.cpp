// A vehicle's breakable parts, through its CVehiclePhysComponent.
#include "engine/parts.h"

#include "engine/memory.h"
#include "fcse_api.h"

namespace {
    using VehicleOverhaul::At;

    // CVehiclePhysComponent: its parts, the part the others hang on, and whether its engine runs.
    constexpr ptrdiff_t kParts = 0x3C;
    constexpr ptrdiff_t kPartCount = 0x40;
    constexpr ptrdiff_t kFramePart = 0x80;
    constexpr ptrdiff_t kEngineRunning = 0x100;

    // CVehiclePhysComponent::CPart: what holds its body, its name, and the state it is in.
    constexpr ptrdiff_t kPartStride = 0x84;
    constexpr ptrdiff_t kPartBody = 0x00;
    constexpr ptrdiff_t kPartName = 0x58;
    constexpr ptrdiff_t kPartState = 0x5C;

    // The frame part's body, a CPhysEntity, answers here the CPhysVehicleEntity it is, or null.
    constexpr size_t kGetVehicleSlot = 0x28 / sizeof(void*);
    // CPhysWheeledVehicleEntity: the impl its physics runs as.
    constexpr ptrdiff_t kVehicleImpl = 0x5C;

    // What CVehiclePhysComponent::GetSavedParts appends for each part, in order.
    struct SavedPart {
        float health;
        uint32_t state;
    };
    struct SavedParts {
        SavedPart* items;
        int32_t count;
    };

    using OnMoveFn = void(__fastcall*)(void* component, void* unused, float seconds);
    using GotoStateFn = void(__fastcall*)(uint8_t* part, void* unused, uint32_t state, void* component,
                                          void* motion, bool quiet);
    using SavedPartsFn = void(__fastcall*)(void* component, void* unused, SavedParts* saved);
    using GetVehicleFn = uint8_t*(__fastcall*)(void* body);

    FCSE::Relocation<OnMoveFn> g_onMove{FCSE::Uplay(0x0006EE10)};
    // CVehiclePhysComponent::CPart::GotoStateIndex, which only ever moves a part on.
    FCSE::Relocation<GotoStateFn> g_gotoState{FCSE::Uplay(0x0006D920)};
    // CVehiclePhysComponent::GetSavedParts, what a save or a vehicle leaving the world keeps.
    FCSE::Relocation<SavedPartsFn> g_savedParts{FCSE::Uplay(0x0006E120)};

    OnMoveFn g_originalOnMove = nullptr;
    SavedPartsFn g_originalSavedParts = nullptr;

    VehicleOverhaul::Parts::MoveFn g_move = nullptr;
    uint32_t g_firstState = 0;

    uint8_t* Part(void* component, int part) { return At<uint8_t*>(component, kParts) + part * kPartStride; }

    void __fastcall OnMoveDetour(void* component, void* unused, float seconds) {
        g_originalOnMove(component, unused, seconds);
        g_move(component);
    }

    void __fastcall SavedPartsDetour(void* component, void* unused, SavedParts* saved) {
        const int32_t first = saved->count;
        g_originalSavedParts(component, unused, saved);
        const int part = VehicleOverhaul::Parts::Find(component, g_firstState);
        if (part >= 0 && first + part < saved->count) {
            saved->items[first + part].state = 0;
        }
    }
}

namespace VehicleOverhaul::Parts {

bool Install(MoveFn move) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_onMove || !g_gotoState) {
        api->Log("parts: the vehicle parts' update was not found in this build");
        return false;
    }
    g_move = move;
    return api->Hook(reinterpret_cast<void*>(g_onMove.address()), reinterpret_cast<void*>(&OnMoveDetour),
                     reinterpret_cast<void**>(&g_originalOnMove));
}

bool SaveInFirstState(uint32_t name) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_savedParts) {
        api->Log("parts: what a save keeps of a vehicle's parts was not found in this build");
        return false;
    }
    g_firstState = name;
    return api->Hook(reinterpret_cast<void*>(g_savedParts.address()), reinterpret_cast<void*>(&SavedPartsDetour),
                     reinterpret_cast<void**>(&g_originalSavedParts));
}

int Find(void* component, uint32_t name) {
    const int count = At<int32_t>(component, kPartCount);
    for (int part = 0; part < count; ++part) {
        if (At<uint32_t>(Part(component, part), kPartName) == name) {
            return part;
        }
    }
    return -1;
}

uint32_t State(void* component, int part) { return At<uint32_t>(Part(component, part), kPartState); }

void Advance(void* component, int part, uint32_t state) {
    g_gotoState(Part(component, part), nullptr, state, component, nullptr, true);
}

bool EngineRunning(void* component) { return At<uint8_t>(component, kEngineRunning) != 0; }

Wheeled::Car Car(void* component) {
    uint8_t* frame = At<uint8_t*>(component, kFramePart);
    if (frame == nullptr) {
        return nullptr;
    }
    void** holder = At<void**>(frame, kPartBody);
    if (holder == nullptr || *holder == nullptr) {
        return nullptr;
    }
    void* body = *holder;
    uint8_t* vehicle = reinterpret_cast<GetVehicleFn>((*reinterpret_cast<void***>(body))[kGetVehicleSlot])(body);
    return vehicle != nullptr ? At<Wheeled::Car>(vehicle, kVehicleImpl) : nullptr;
}

}
