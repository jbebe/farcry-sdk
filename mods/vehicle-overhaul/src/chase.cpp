// The chase camera: behind the vehicle, swinging after its heading, orbited by the mouse.
#include "chase.h"

#include "engine/terrain.h"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace {
    constexpr float kPi = std::numbers::pi_v<float>;

    constexpr float Radians(float degrees) { return degrees * kPi / 180.0f; }

    constexpr float kDistance = 6.0f;
    // The aim point above the vehicle's origin, in metres.
    constexpr float kAimHeight = 1.6f;
    constexpr float kRestPitch = Radians(-12.0f);
    constexpr float kLowestPitch = Radians(-75.0f);
    constexpr float kHighestPitch = Radians(20.0f);
    // The least room between the camera and the ground under it.
    constexpr float kClearance = 0.5f;

    // Per second: how fast the camera swings behind a turning car, and the aim point catches up with
    // its height.
    constexpr float kHeadingRate = 4.0f;
    constexpr float kHeightRate = 6.0f;
    // Seconds without mouse look before the camera eases back behind the car, and how fast it does.
    constexpr float kIdleBeforeReturn = 2.0f;
    constexpr float kReturnRate = 2.0f;

    // Everything the camera carries between frames; heading and height are seeded by the first one.
    struct State {
        bool started = false;
        float heading = 0.0f;
        float aimZ = 0.0f;
        float orbit = 0.0f;
        float pitch = kRestPitch;
        float idle = 0.0f;
        float lookPitch = 0.0f;
        float lookYaw = 0.0f;
    };

    State g_state;

    float Wrap(float radians) { return std::remainder(radians, 2.0f * kPi); }

    // The share of the remaining distance to close in `seconds` at `rate`, whatever the frame rate.
    float Approach(float rate, float seconds) { return 1.0f - std::exp(-rate * seconds); }
}

namespace VehicleOverhaul::Chase {

void Reset() { g_state = {}; }

void Look(float pitch, float yaw) {
    g_state.lookPitch = pitch;
    g_state.lookYaw = yaw;
}

ThirdCamera::Pose Place(const float* vehicle, float seconds) {
    State& s = g_state;

    // Local +Y is forward, hence the quarter turn.
    const float heading = std::atan2(vehicle[5], vehicle[4]) - kPi / 2.0f;
    if (!s.started) {
        s.heading = heading;
        s.aimZ = vehicle[14];
        s.started = true;
    }
    s.heading = Wrap(s.heading + Wrap(heading - s.heading) * Approach(kHeadingRate, seconds));
    s.aimZ += (vehicle[14] - s.aimZ) * Approach(kHeightRate, seconds);

    if (s.lookPitch != 0.0f || s.lookYaw != 0.0f) {
        s.orbit = Wrap(s.orbit - s.lookYaw * seconds * kPi);
        s.pitch = std::clamp(s.pitch + s.lookPitch * seconds * kPi, kLowestPitch, kHighestPitch);
        s.idle = 0.0f;
    } else if ((s.idle += seconds) > kIdleBeforeReturn) {
        const float share = Approach(kReturnRate, seconds);
        s.orbit -= s.orbit * share;
        s.pitch += (kRestPitch - s.pitch) * share;
    }
    s.lookPitch = 0.0f;
    s.lookYaw = 0.0f;

    const float yaw = s.heading + s.orbit;
    const float aim[3] = {vehicle[12], vehicle[13], s.aimZ + kAimHeight};
    const float across = kDistance * std::cos(s.pitch);

    // Back along the view from the aim point; a view at yaw 0 looks down +Y.
    ThirdCamera::Pose pose{{aim[0] + std::sin(yaw) * across, aim[1] - std::cos(yaw) * across,
                            aim[2] - kDistance * std::sin(s.pitch)},
                           {s.pitch, 0.0f, yaw}};

    const float floor = Terrain::Height(pose.position[0], pose.position[1]) + kClearance;
    if (pose.position[2] < floor) {
        pose.position[2] = floor;
        pose.angles[0] = std::atan2(aim[2] - floor, across);
    }
    return pose;
}

}
