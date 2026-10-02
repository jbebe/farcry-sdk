// The third-person camera in a vehicle, switched by the Third-person view control. Leaving the
// vehicle puts the first-person camera back.
#include "third_person.h"

#include "crc32.h"
#include "engine/camera.h"
#include "engine/input.h"
#include "engine/pawn_tick.h"
#include "engine/vehicle.h"
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    constexpr uint32_t kToggleSignal = VehicleOverhaul::Crc32("active_camerathird");

    // Counted on the dispatcher, spent on the pawn tick.
    std::atomic<int> g_toggles{0};

    // Whether the player asked for the view, and the camera while it is up.
    bool g_wanted = false;
    void* g_camera = nullptr;

    void OnSignal(uint32_t signal) {
        if (signal == kToggleSignal) {
            ++g_toggles;
        }
    }

    void Enter(void* manager) {
        void* previous = VehicleOverhaul::Camera::Active(manager);
        VehicleOverhaul::Camera::ActivateByName(manager, VehicleOverhaul::Camera::kThird);
        void* camera = VehicleOverhaul::Camera::Active(manager);

        if (camera == nullptr || camera == previous) {
            FCSE::Logf("third person: the manager would not put up %s", VehicleOverhaul::Camera::kThird);
            g_wanted = false;
            return;
        }
        g_camera = camera;
        FCSE::Logf("third person: %s is up (camera %p, was %p)", VehicleOverhaul::Camera::kThird, camera,
                   previous);
    }

    void Leave(void* manager) {
        VehicleOverhaul::Camera::ActivateByName(manager, VehicleOverhaul::Camera::kFirst);
        if (VehicleOverhaul::Camera::Active(manager) != g_camera) {
            g_camera = nullptr;
        }
    }

    void Tick(void* pawn) {
        if (g_toggles.exchange(0) % 2 != 0) {
            g_wanted = !g_wanted;
        }
        if (!g_wanted && g_camera == nullptr) {
            return;
        }

        void* manager = VehicleOverhaul::Camera::Manager();

        // A level load or a cutscene took the camera: let it go where it stands.
        if (g_camera != nullptr && VehicleOverhaul::Camera::Active(manager) != g_camera) {
            FCSE::Logf("third person: the camera was taken over, leaving it");
            g_camera = nullptr;
            g_wanted = false;
            return;
        }

        if (VehicleOverhaul::Vehicle::Current(pawn) == nullptr) {
            g_wanted = false;
        }
        if (manager == nullptr || VehicleOverhaul::Camera::Locked(manager) ||
            g_wanted == (g_camera != nullptr)) {
            return;
        }
        if (g_wanted) {
            Enter(manager);
        } else {
            Leave(manager);
        }
    }
}

namespace VehicleOverhaul::ThirdPerson {

bool Install() {
    if (!Camera::Install() || !Vehicle::Install() || !PawnTick::Install(&Tick)) {
        return false;
    }
    // A hook is live from here, so the plugin has to stay loaded even without the control.
    Input::Install(&OnSignal);
    return true;
}

}
