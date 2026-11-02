// The gun brought up after a sprint. See ready.h.
#include "ready.h"

#include "ease.h"

#include <atomic>

namespace {
    // Seconds to bring a gun up after a sprint: the game's own animation takes the first, and each
    // kilogram the gun weighs adds the second. Seconds for the gun to go low as a sprint starts.
    constexpr float kRaise = 0.25f;
    constexpr float kRaisePerKilogram = 0.03f;
    constexpr float kLower = 0.2f;
    // How far the gun is held low, in radians down and in metres.
    constexpr float kLoweredPitch = 0.35f;
    constexpr float kLoweredDrop = 0.04f;

    std::atomic<bool> g_enabled{true};
    // How far the gun is lowered, from nought to one.
    float g_lowered = 0.0f;
}

void AimingOverhaul::Ready::Update(float seconds, bool sprinting, float kilograms) {
    if (!g_enabled) {
        g_lowered = 0.0f;
        return;
    }
    const float span = sprinting ? kLower : kRaise + kRaisePerKilogram * kilograms;
    g_lowered = Approach(g_lowered, sprinting ? 1.0f : 0.0f, seconds / span);
}

bool AimingOverhaul::Ready::Lowered() {
    return g_lowered > 0.0f;
}

AimingOverhaul::Body::Offset AimingOverhaul::Ready::Posed() {
    const float lowered = Ease(g_lowered);
    return {0.0f, -kLoweredPitch * lowered, 0.0f, 0.0f, 0.0f, -kLoweredDrop * lowered};
}

void AimingOverhaul::Ready::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
