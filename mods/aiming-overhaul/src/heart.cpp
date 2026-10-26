// The heart: its rate climbs with exertion and comes back down as the stamina does, jumps with a
// wound or a blast nearby and settles slowly from that; each beat jolts the aim, and a fast heart
// steadies the hands less.
#include "heart.h"

#include "ease.h"
#include "stance.h"

#include <algorithm>
#include <cmath>

namespace {
    // Beats a minute at rest and at the fastest.
    constexpr float kRest = 70.0f;
    constexpr float kFastest = 170.0f;
    // Where exertion takes the heart: all the way with no stamina left, and this far past the
    // stamina's own share while sprinting and while walking upright.
    constexpr float kSprinting = 0.25f;
    constexpr float kWalking = 0.1f;
    // Seconds for exertion to raise the heart, and to let it down as the stamina comes back, quick
    // enough that it is at rest soon after the stamina is full.
    constexpr float kClimb = 2.5f;
    constexpr float kEase = 1.0f;
    // Beats a minute added for all of the health lost at once, and for each radian a second the
    // camera is shaken; and seconds for what they add to settle.
    constexpr float kPerHealth = 120.0f;
    constexpr float kPerShake = 1500.0f;
    constexpr float kSettle = 20.0f;
    // Seconds a beat's jolt takes to reach its height.
    constexpr float kJoltRise = 0.05f;

    // Beats a minute above rest from exertion and from wounds and blasts, the rate they make, and
    // the health last seen, below nought before any.
    float g_exerted = 0.0f;
    float g_shaken = 0.0f;
    float g_rate = kRest;
    float g_health = -1.0f;
    // Seconds to the next beat, whether one fell this frame, and seconds since the last.
    float g_toBeat = 0.0f;
    bool g_beat = false;
    float g_sinceBeat = 1.0f;
}

void AimingOverhaul::Heart::Update(float seconds, bool sprinting, float stamina, float health,
                                   float shake) {
    // Walking upright keeps the heart up a little.
    const float upright = 1.0f - Stance::Crouched();
    const float exertion = std::clamp(1.0f - stamina + (sprinting ? kSprinting : 0.0f) +
                                          kWalking * Stance::Moving() * upright,
                                      0.0f, 1.0f);
    const float exerted = (kFastest - kRest) * exertion;
    g_exerted = Follow(g_exerted, exerted, seconds, g_exerted < exerted ? kClimb : kEase);
    // A wound, but not the health coming back, the first health seen, or a new life; and the camera
    // shaking. Crouched, that settles twice as fast standing still, and holds on the move.
    const float calming = (1.0f - Stance::Moving()) * (2.0f - upright);
    g_shaken = Follow(g_shaken, 0.0f, seconds * calming, kSettle);
    if (g_health >= 0.0f && health < g_health) {
        g_shaken += kPerHealth * (g_health - health);
    }
    g_health = health;
    g_shaken = (std::min)(g_shaken + kPerShake * shake * seconds, kFastest - kRest);
    g_rate = (std::min)(kRest + g_exerted + g_shaken, kFastest);

    g_sinceBeat += seconds;
    g_toBeat -= seconds;
    g_beat = g_toBeat <= 0.0f;
    if (g_beat) {
        g_toBeat += 60.0f / g_rate;
        g_toBeat = (std::max)(g_toBeat, 0.0f);
        g_sinceBeat = 0.0f;
    }
}

float AimingOverhaul::Heart::Rate() {
    return g_rate;
}

float AimingOverhaul::Heart::Racing() {
    return std::clamp((g_rate - kRest) / (kFastest - kRest), 0.0f, 1.0f);
}

bool AimingOverhaul::Heart::Beat() {
    return g_beat;
}

float AimingOverhaul::Heart::Jolt() {
    const float t = g_sinceBeat / kJoltRise;
    return t * std::exp(1.0f - t);
}
