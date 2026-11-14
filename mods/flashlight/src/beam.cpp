#include "beam.h"

#include "fcse_api.h"

#include <cstdint>
#include <cstring>

namespace {
    constexpr char kChannel[] = "flashlight.beam";
    constexpr uint32_t kVersion = 1;

    // The message: whether the light is on, the lamp's position and direction in the world, its
    // range in metres and its full cone angle in radians.
    struct Message {
        uint32_t version;
        uint32_t lit;
        float position[3];
        float direction[3];
        float range;
        float coneAngle;
    };

    bool g_lit = false;

    void Post(const Message& message) { FCSE::ApiPointer()->Send(kChannel, &message, sizeof(message)); }
}

namespace Flashlight::Beam {

void Send(const Light::Pose& pose, const Light::Spot& spot) {
    Message message = {kVersion, 1, {}, {}, spot.range, spot.outerAngle};
    std::memcpy(message.position, pose.position, sizeof(message.position));
    std::memcpy(message.direction, pose.direction, sizeof(message.direction));
    Post(message);
    g_lit = true;
}

void Off() {
    if (g_lit) {
        Post(Message{kVersion, 0, {}, {}, 0.0f, 0.0f});
        g_lit = false;
    }
}

}
