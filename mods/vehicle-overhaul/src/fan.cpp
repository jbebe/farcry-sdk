// The big truck's radiator fan, spun by a Havok motor while the player's engine runs.
#include "fan.h"

#include "engine/hinge.h"
#include "engine/parts.h"
#include "engine/wheeled.h"
#include "fcse_api.h"

namespace {
    using namespace VehicleOverhaul;

    constexpr uint32_t kFan = FCSE::Crc32("Fan");
    constexpr uint32_t kHinged = 1;

    // Whether the player's truck runs its engine, which every hinged fan's shared motor follows.
    bool g_motor = false;

    void OnMove(void* component) {
        const int fan = Parts::Find(component, kFan);
        if (fan < 0) {
            return;
        }
        const Wheeled::Car car = Parts::Car(component);
        const Wheeled::Car player = Wheeled::Player();
        if (player == nullptr) {
            g_motor = false;
        } else if (car == player) {
            g_motor = Parts::EngineRunning(component);
        }

        if (car == player && g_motor && Parts::State(component, fan) < kHinged) {
            Parts::Advance(component, fan, kHinged);
        }
        if (car != nullptr && Parts::State(component, fan) == kHinged) {
            if (uint8_t* hinge = Hinge::WithVelocityMotor(Wheeled::ChassisBody(car))) {
                Hinge::SetMotor(hinge, g_motor);
            }
        }
    }
}

namespace VehicleOverhaul::Fan {

bool Install() { return Parts::Install(&OnMove) && Parts::SaveInFirstState(kFan); }

}
