// How the player stands: crouched or not, still or moving, which steadies or unsteadies the hands.
// A lean keeps the stance it leans from, and nothing counts as resting the gun.
#include "stance.h"

#include "ease.h"
#include "engine/pawn.h"

#include <algorithm>
#include <cmath>

namespace {
    // The sway left standing still, crouched still, standing moving and crouched moving.
    constexpr float kStanding = 1.0f;
    constexpr float kCrouched = 0.5f;
    constexpr float kStandingMoving = 1.8f;
    constexpr float kCrouchedMoving = 1.4f;
    // Metres a second along the ground below which the player is still, a creep included, and from
    // which they are wholly moving.
    constexpr float kStill = 0.4f;
    constexpr float kMoving = 1.5f;
    // Seconds for the stance to ease between crouched and standing, and still and moving.
    constexpr float kFollow = 0.15f;
    // Seconds between two frames past which the step is too long to measure a speed by.
    constexpr float kLongestStep = 0.25f;

    uint8_t* g_pawn = nullptr;
    float g_place[3] = {};
    float g_crouched = 0.0f;
    float g_moving = 0.0f;
}

void AimingOverhaul::Stance::Update(uint8_t* pawn, float seconds) {
    g_crouched = Follow(g_crouched, Pawn::Crouched(pawn) ? 1.0f : 0.0f, seconds, kFollow);
    float place[3];
    if (!Pawn::Position(pawn, place)) {
        return;
    }
    // A new pawn, or a long frame such as a load, gives no speed to measure.
    if (pawn == g_pawn && seconds > 0.0f && seconds < kLongestStep) {
        const float speed = std::hypot(place[0] - g_place[0], place[1] - g_place[1]) / seconds;
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

float AimingOverhaul::Stance::Sway() {
    const float still = kStanding + (kCrouched - kStanding) * g_crouched;
    const float moving = kStandingMoving + (kCrouchedMoving - kStandingMoving) * g_crouched;
    return still + (moving - still) * g_moving;
}
