// The vehicle a pawn sits in.
#include "engine/vehicle.h"

#include "fcse_api.h"

namespace {
    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);

    FCSE::Relocation<GetCurrentVehicleFn> g_getCurrentVehicle{FCSE::Uplay(0x000E7330)};
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

}
