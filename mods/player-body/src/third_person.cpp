// The player seen over the right shoulder on foot. See third_person.h.
#include "third_person.h"

#include "engine/camera.h"
#include "engine/pawn.h"
#include "engine/third_camera.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>

namespace {
    namespace Camera = PlayerBody::Camera;
    namespace Pawn = PlayerBody::Pawn;

    // In metres: how high above the feet the camera turns about, and how far it stands right of
    // that point and back from it along the view.
    constexpr float kShoulder = 1.6f;
    constexpr float kRight = 0.55f;
    constexpr float kBack = 1.6f;

    std::atomic<bool> g_enabled{false};
    // The third-person camera while it is up, and the pawn it follows. Game thread.
    uint8_t* g_camera = nullptr;
    uint8_t* g_pawn = nullptr;

    bool Place(uint8_t* camera, float, PlayerBody::ThirdCamera::Pose& pose) {
        uint8_t* entity = camera == g_camera && g_pawn != nullptr ? Pawn::Entity(g_pawn) : nullptr;
        if (entity == nullptr) {
            return false;
        }
        const float* look = Pawn::Look(g_pawn);
        const float pitch = look[0];
        const float yaw = look[2];
        const float forward[3] = {-std::sin(yaw) * std::cos(pitch), std::cos(yaw) * std::cos(pitch),
                                  std::sin(pitch)};
        const float right[3] = {std::cos(yaw), std::sin(yaw), 0.0f};
        const float* feet = Pawn::Place(entity);
        for (int k = 0; k < 3; ++k) {
            pose.position[k] = feet[k] + right[k] * kRight - forward[k] * kBack;
        }
        pose.position[2] += kShoulder;
        pose.angles[0] = pitch;
        pose.angles[1] = 0.0f;
        pose.angles[2] = yaw;
        return true;
    }
}

bool PlayerBody::ThirdPerson::Install() {
    return ThirdCamera::Install(&Place);
}

void PlayerBody::ThirdPerson::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void PlayerBody::ThirdPerson::Update(uint8_t* pawn) {
    if (pawn != g_pawn) {
        g_camera = nullptr;
        g_pawn = pawn;
    }
    const bool wanted = g_enabled && Pawn::OnFoot(pawn);
    if (wanted == (g_camera != nullptr)) {
        return;
    }
    uint8_t* camera = Camera::Activate(wanted ? Camera::kThird : Camera::kFirst);
    if (camera == nullptr) {
        return;
    }
    Pawn::SetFirstPerson(pawn, !wanted);
    g_camera = wanted ? camera : nullptr;
    FCSE::Logf("third person: %s", wanted ? "up" : "down");
}
