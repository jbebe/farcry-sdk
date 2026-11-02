// A landing's drop of the eye. See landing.h.
#include "landing.h"

#include "ease.h"
#include "engine/pawn.h"
#include "stance.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    // The eye's spring, in radians a second, and how damped it is; how much of the speed landed at
    // drops it, and the most it drops, in metres.
    constexpr float kStiffness = 14.0f;
    constexpr float kDamping = 0.75f;
    constexpr float kDropPerSpeed = 0.35f;
    constexpr float kMostDrop = 0.12f;
    // Metres above the ground past which the pawn is in the air, and metres a second past which a
    // frame's move is a cut rather than a fall.
    constexpr float kAirborne = 0.1f;
    constexpr float kFastestMove = 40.0f;

    std::atomic<bool> g_enabled{true};
    // The pawn last seen, whether it was in the air and how fast it was falling, in metres a second;
    // and how far the eye has dropped.
    uint8_t* g_pawn = nullptr;
    bool g_airborne = false;
    float g_falling = 0.0f;
    AimingOverhaul::Spring g_drop;
}

void AimingOverhaul::Landing::Update(uint8_t* pawn, float seconds) {
    if (pawn != g_pawn || seconds <= 0.0f) {
        g_pawn = pawn;
        g_airborne = false;
        g_falling = 0.0f;
        g_drop = {};
        return;
    }
    float velocity[3];
    const bool measured = Stance::Velocity(velocity) &&
                          std::hypot(velocity[0], velocity[1], velocity[2]) < kFastestMove;
    const bool airborne = Pawn::AboveGround(pawn) > kAirborne || Pawn::Jumping(pawn);
    if (measured && g_airborne && !airborne && g_enabled) {
        g_drop.v += kDropPerSpeed * (std::max)(-g_falling, 0.0f);
    }
    g_airborne = measured && airborne;
    g_falling = measured ? velocity[2] : 0.0f;
    g_drop.Step(0.0f, kStiffness, kDamping, seconds);
}

float AimingOverhaul::Landing::EyeDrop() {
    return kMostDrop * std::tanh(g_drop.x / kMostDrop);
}

void AimingOverhaul::Landing::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
