// How the player stands. See stance.h.
#include "stance.h"

#include "ease.h"
#include "engine/pawn.h"

#include <algorithm>
#include <cmath>

namespace {
    // The share of the sway left crouched still, and how many times standing still's is left
    // standing moving.
    constexpr float kCrouched = 0.5f;
    constexpr float kStandingMoving = 2.0f;
    // Metres a second along the ground below which the player is still, a creep included, and from
    // which they are wholly moving, a crouched walk down the sights included.
    constexpr float kStill = 0.4f;
    constexpr float kMoving = 1.0f;
    // Seconds for the stance to ease between crouched and standing, and still and moving.
    constexpr float kFollow = 0.15f;
    // Seconds between two frames past which the step is too long to measure a speed by.
    constexpr float kLongestStep = 0.25f;

    uint8_t* g_pawn = nullptr;
    float g_place[3] = {};
    float g_velocity[3] = {};
    bool g_measured = false;
    float g_crouched = 0.0f;
    float g_moving = 0.0f;
}

void AimingOverhaul::Stance::Update(uint8_t* pawn, float seconds) {
    g_crouched = Follow(g_crouched, Pawn::Crouched(pawn) ? 1.0f : 0.0f, seconds, kFollow);
    g_measured = false;
    float place[3];
    if (!Pawn::Position(pawn, place)) {
        return;
    }
    // A new pawn, or a long frame such as a load, gives no speed to measure.
    if (pawn == g_pawn && seconds > 0.0f && seconds < kLongestStep) {
        for (int k = 0; k < 3; ++k) {
            g_velocity[k] = (place[k] - g_place[k]) / seconds;
        }
        g_measured = true;
        const float speed = std::hypot(g_velocity[0], g_velocity[1]);
        const float moving = std::clamp((speed - kStill) / (kMoving - kStill), 0.0f, 1.0f);
        g_moving = Follow(g_moving, moving, seconds, kFollow);
    }
    g_pawn = pawn;
    std::copy_n(place, 3, g_place);
}

float AimingOverhaul::Stance::Crouched() {
    return g_crouched;
}

float AimingOverhaul::Stance::Moving() {
    return g_moving;
}

bool AimingOverhaul::Stance::Velocity(float out[3]) {
    std::copy_n(g_velocity, 3, out);
    return g_measured;
}

float AimingOverhaul::Stance::Sway(float standing, float crouched) {
    const float still = std::lerp(standing, kCrouched * crouched, g_crouched);
    const float moving = std::lerp(kStandingMoving * standing, standing, g_crouched);
    return std::lerp(still, moving, g_moving);
}
