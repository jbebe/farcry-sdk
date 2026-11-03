// The game clock.
//
// See docs/docs/engine-internals/architecture.md#the-game-clock.
#include "engine/clock.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    // CTimer, through the pointer the engine keeps, and two of its doubles: the clamped real delta,
    // and the scale the frame time is that delta times, 1 as the engine leaves it.
    FCSE::Relocation<uint8_t* const*> g_timer{FCSE::Uplay(0x01606360)};
    constexpr ptrdiff_t kRealDelta = 0x40;
    constexpr ptrdiff_t kScale = 0x50;

    double* Field(ptrdiff_t offset) {
        uint8_t* timer = g_timer ? *g_timer.get() : nullptr;
        return timer != nullptr ? reinterpret_cast<double*>(timer + offset) : nullptr;
    }
}

bool DevTools::Clock::Found() { return static_cast<bool>(g_timer); }

void DevTools::Clock::SetStopped(bool stopped) {
    if (double* scale = Field(kScale)) {
        *scale = stopped ? 0.0 : 1.0;
    }
}

bool DevTools::Clock::Stopped() {
    const double* scale = Field(kScale);
    return scale != nullptr && *scale == 0.0;
}

float DevTools::Clock::RealDelta() {
    const double* delta = Field(kRealDelta);
    return delta != nullptr ? static_cast<float>(*delta) : 0.0f;
}
