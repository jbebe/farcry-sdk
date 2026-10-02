// The third-person camera in a vehicle, switched by the Third-person view control. Leaving the
// vehicle puts the first-person camera back.
//
// The engine's Cameras.Camera.Third is put up and then placed by the chase camera, which takes the
// mouse look for itself while it is up.
#include "third_person.h"

#include "chase.h"
#include "crc32.h"
#include "engine/camera.h"
#include "engine/entity.h"
#include "engine/input.h"
#include "engine/pawn_tick.h"
#include "engine/terrain.h"
#include "engine/third_camera.h"
#include "engine/vehicle.h"
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    constexpr uint32_t kToggleSignal = VehicleOverhaul::Crc32("active_camerathird");

    // Counted on the dispatcher, spent on the pawn tick.
    std::atomic<int> g_toggles{0};

    // Whether the player asked for the view, the camera while it is up, and the pawn it follows.
    bool g_wanted = false;
    void* g_camera = nullptr;
    void* g_pawn = nullptr;

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
        VehicleOverhaul::Chase::Reset();
    }

    void Leave(void* manager) {
        VehicleOverhaul::Camera::ActivateByName(manager, VehicleOverhaul::Camera::kFirst);
        if (VehicleOverhaul::Camera::Active(manager) != g_camera) {
            g_camera = nullptr;
        }
    }

    void Tick(void* pawn, float* look) {
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

        g_pawn = pawn;
        if (VehicleOverhaul::Vehicle::Current(pawn) == nullptr) {
            g_wanted = false;
        }
        // The driver's own view stays where it was while the chase camera has the mouse.
        if (g_camera != nullptr) {
            VehicleOverhaul::Chase::Look(look[0], look[1]);
            look[0] = 0.0f;
            look[1] = 0.0f;
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

    bool Place(void* camera, float seconds, VehicleOverhaul::ThirdCamera::Pose& pose) {
        void* vehicle =
            camera == g_camera ? VehicleOverhaul::Entity::Of(VehicleOverhaul::Vehicle::Current(g_pawn)) : nullptr;
        if (vehicle == nullptr) {
            return false;
        }
        pose = VehicleOverhaul::Chase::Place(VehicleOverhaul::Entity::Matrix(vehicle), seconds);
        return true;
    }
}

namespace VehicleOverhaul::ThirdPerson {

bool Install() {
    if (!Camera::Install() || !Vehicle::Install() || !Entity::Install() || !Terrain::Install() ||
        !ThirdCamera::Install(&Place)) {
        return false;
    }
    // A hook is live from here, so the plugin has to stay loaded whatever happens next.
    PawnTick::Install(&Tick);
    Input::Install(&OnSignal);
    return true;
}

}
