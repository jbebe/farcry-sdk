#include "engine/driver.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    // CPhysWheeledVehicleEntityImpl::SetDriver(driver), __thiscall on the car, 1 for a human. Its
    // first instructions are wildcards because Vehicle Overhaul hooks it too.
    using SetDriverFn = void(__fastcall*)(uint8_t* car, void* unused, int32_t driver);
    FCSE::Relocation<SetDriverFn> g_setDriver{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? 57 8B F1 E8 ?? ?? ?? ?? 83 FF 02 75 1F 8B 86 84 01 00 00 8B 48 24 C6 41 3D "
        "01")};
    SetDriverFn g_original = nullptr;
    constexpr int32_t kHuman = 1;

    // The car the player drives, or null.
    std::atomic<uint8_t*> g_car{nullptr};

    void __fastcall SetDriverDetour(uint8_t* car, void* unused, int32_t driver) {
        g_original(car, unused, driver);
        if (driver == kHuman) {
            g_car = car;
        } else {
            uint8_t* driven = car;
            g_car.compare_exchange_strong(driven, nullptr);
        }
    }
}

bool ThirdPerson::Driver::Install() {
    if (!g_setDriver) {
        FCSE::ApiPointer()->Log("driver: the call that gives a car its driver was not found");
        return false;
    }
    return Hook(g_setDriver.address(), &SetDriverDetour, &g_original);
}

bool ThirdPerson::Driver::Driving() {
    return g_car != nullptr;
}
