// The vehicle a pawn sits in.
#include "engine/vehicle.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);

    FCSE::Relocation<GetCurrentVehicleFn> g_getCurrentVehicle{FCSE::Uplay(0x000E7330)};

    // CVehicle's properties object, which RegisterProperties files iAnimVehicleType at +4 of; measured
    // in game on the Datsun, Wrangler, Rover and buggy (2026-10-16).
    constexpr ptrdiff_t kVehicleProperties = 0x10;
    constexpr ptrdiff_t kPropertiesAnimType = 0x04;
}

namespace VehicleOverhaul::Vehicle {

bool Install() {
    if (!g_getCurrentVehicle) {
        FCSE::ApiPointer()->Log("vehicle: CVehicle::GetCurrentVehicle was not found in this build");
        return false;
    }
    return true;
}

void* Current(void* pawn) { return pawn != nullptr ? g_getCurrentVehicle(pawn) : nullptr; }

int AnimType(void* vehicle) {
    const uint8_t* properties =
        vehicle != nullptr ? *reinterpret_cast<uint8_t* const*>(static_cast<uint8_t*>(vehicle) + kVehicleProperties)
                           : nullptr;
    return properties != nullptr ? *reinterpret_cast<const int32_t*>(properties + kPropertiesAnimType) : -1;
}

}
