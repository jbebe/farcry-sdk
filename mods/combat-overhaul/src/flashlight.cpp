// The player's flashlight: the beam the Flashlight plugin sends, kept for the perception model.
#include "flashlight.h"

#include "engine/game.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstring>
#include <mutex>

using namespace CombatOverhaul;

namespace {
    constexpr char kChannel[] = "flashlight.beam";
    constexpr uint32_t kVersion = 1;

    // Flashlight's message: whether the light is on, the lamp's position and direction in the world,
    // its range in metres and its full cone angle in radians.
    struct Message {
        uint32_t version;
        uint32_t lit;
        float position[3];
        float direction[3];
        float range;
        float coneAngle;
    };

    struct Beam {
        Message message = {};
        float cosHalfAngle = 1.0f;
    };

    // Set on the game's frame, read by every soldier's vision update.
    std::mutex g_lock;
    Beam g_beam;

    void OnMessage(const char*, const void* data, size_t size, void*) {
        Message message;
        if (size != sizeof(message)) {
            return;
        }
        std::memcpy(&message, data, sizeof(message));
        if (message.version != kVersion) {
            return;
        }
        std::lock_guard<std::mutex> lock(g_lock);
        g_beam = {message, std::cos(message.coneAngle / 2.0f)};
    }
}

Flashlight::Lamp Flashlight::Seen(const float* head) {
    Beam beam;
    {
        std::lock_guard<std::mutex> lock(g_lock);
        beam = g_beam;
    }
    const Message& sent = beam.message;
    Lamp lamp;
    if (sent.lit == 0) {
        return lamp;
    }
    lamp.lit = true;
    float along = 0.0f;
    for (int axis = 0; axis < 3; ++axis) {
        along += (head[axis] - sent.position[axis]) * sent.direction[axis];
    }
    lamp.distance = Game::Distance(head, sent.position);
    lamp.inBeam = lamp.distance <= sent.range && along >= beam.cosHalfAngle * lamp.distance;
    return lamp;
}

void InstallFlashlight() {
    if (!FCSE::ApiPointer()->Listen(kChannel, &OnMessage, nullptr)) {
        FCSE::Logf("flashlight: its beam channel could not be listened on, so the light gives no one away");
    }
}
