// The third-person camera in a vehicle, switched by the Third-person view control. Leaving the
// vehicle puts the first-person camera back; the view chosen is kept for the next vehicle, for the session.
//
// The engine's Cameras.Camera.Third is put up and then placed by the chase camera, which takes the
// mouse look for itself while it is up. The player's full body is drawn while it is up, the first-person
// avatar otherwise.
#include "third_person.h"

#include "chase.h"
#include "engine/camera.h"
#include "engine/entity.h"
#include "engine/input.h"
#include "engine/pawn_tick.h"
#include "engine/terrain.h"
#include "engine/third_camera.h"
#include "engine/vehicle.h"
#include "engine/view_mode.h"
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    constexpr uint32_t kToggleSignal = FCSE::Crc32("active_camerathird");

    // Counted on the dispatcher, spent on the pawn tick.
    std::atomic<int> g_toggles{0};

    // The view the player chose, kept for the game session: every vehicle they get into starts in it. Held is set
    // while the game has taken the camera, until they next get in or press the control.
    bool g_thirdPerson = false;
    bool g_held = false;
    // The camera while it is up, and the pawn it follows.
    void* g_camera = nullptr;
    void* g_pawn = nullptr;

    // Whether the view mode can be switched, and the pawn last drawn with its full body. A pawn the plugin has
    // not switched is drawn as the engine left it, from inside.
    bool g_bodies = false;
    void* g_fullBody = nullptr;

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
            g_held = true;
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

    void Update(void* pawn, float* look) {
        const bool seated = VehicleOverhaul::Vehicle::Current(pawn) != nullptr;
        // On foot the control does nothing: retail's F2 sends it too. Held, it brings the chosen view back.
        if (g_toggles.exchange(0) % 2 != 0 && seated) {
            if (!g_held) {
                g_thirdPerson = !g_thirdPerson;
            }
            g_held = false;
        }
        if (!seated) {
            g_held = false;
        }
        if (!g_thirdPerson && g_camera == nullptr) {
            return;
        }

        void* manager = VehicleOverhaul::Camera::Manager();

        // A level load or a cutscene took the camera: let it go where it stands.
        if (g_camera != nullptr && VehicleOverhaul::Camera::Active(manager) != g_camera) {
            FCSE::Logf("third person: the camera was taken over, leaving it");
            g_camera = nullptr;
            g_held = true;
            return;
        }

        g_pawn = pawn;
        // The driver's own view stays where it was while the chase camera has the mouse.
        if (g_camera != nullptr) {
            VehicleOverhaul::Chase::Look(look[0], look[1]);
            look[0] = 0.0f;
            look[1] = 0.0f;
        }
        const bool wanted = g_thirdPerson && seated && !g_held;
        if (manager == nullptr || VehicleOverhaul::Camera::Locked(manager) || wanted == (g_camera != nullptr)) {
            return;
        }
        if (wanted) {
            Enter(manager);
        } else {
            Leave(manager);
        }
    }

    // The full body while the chase camera is up. Only the pawn in hand is ever switched, so one a level load
    // replaced is left alone.
    void ShowBody(void* pawn) {
        const bool outside = g_camera != nullptr;
        if (!g_bodies || outside == (g_fullBody == pawn)) {
            return;
        }
        if (outside) {
            VehicleOverhaul::ViewMode::Set(pawn, false);
            g_fullBody = pawn;
        } else {
            VehicleOverhaul::ViewMode::Set(pawn, true);
            g_fullBody = nullptr;
        }
    }

    void Tick(void* pawn, float* look) {
        Update(pawn, look);
        ShowBody(pawn);
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
    // Without it the chase camera still works, over the first-person avatar.
    g_bodies = ViewMode::Install();
    // A hook is live from here, so the plugin has to stay loaded whatever happens next.
    PawnTick::Subscribe(&Tick);
    Input::Install(&OnSignal);
    return true;
}

}
