#include "engine/time_of_day.h"

#include "devtools_api.h"
#include "fcse_api.h"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace {
    // Inside the environment manager.
    constexpr size_t kTimeScale = 0x25C;
    constexpr size_t kSecondsFromMidnight = 0x260;
    constexpr size_t kCumulatedDays = 0x4EC;

    constexpr double kDay = 86400.0;
    constexpr double kHour = 3600.0;

    // The frame's call to the manager's Update, whose `this` is loaded from the manager's global by
    // the `mov ecx` 16 bytes in.
    constexpr ptrdiff_t kManagerGlobal = 16;
    FCSE::Relocation<uint8_t*> g_updateCall{FCSE::Pattern(
        "8B 35 ?? ?? ?? ?? 51 8B CE E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? "
        "F2 0F 10 42 38")};

    // Where the manager's pointer is kept, or null before Install found it.
    const uint8_t* const* g_manager = nullptr;

    // What was last returned, and the read before it.
    double g_elapsed = -1.0;
    double g_previous = -1.0;

    const uint8_t* Manager() {
        return g_manager != nullptr ? *g_manager : nullptr;
    }
}

bool SkyOverhaul::TimeOfDay::Install() {
    if (!g_updateCall) {
        FCSE::Logf("time of day: the environment manager was not found in this build");
        return false;
    }
    g_manager = *reinterpret_cast<const uint8_t* const**>(g_updateCall.address() + kManagerGlobal);
    return true;
}

bool SkyOverhaul::TimeOfDay::Elapsed(double& seconds) {
    const uint8_t* manager = Manager();
    if (manager == nullptr) {
        return false;
    }
    const double read = *reinterpret_cast<const uint32_t*>(manager + kCumulatedDays) * kDay +
                        *reinterpret_cast<const float*>(manager + kSecondsFromMidnight);

    // A jump of more than an hour is taken only once a second read agrees with it: the game counts a
    // day a moment before it winds the clock back.
    if (g_elapsed < 0.0 || std::abs(read - g_elapsed) < kHour || std::abs(read - g_previous) < kHour) {
        g_elapsed = read;
    }
    g_previous = read;
    seconds = g_elapsed;
    return true;
}

float SkyOverhaul::TimeOfDay::Scale() {
    const uint8_t* manager = Manager();
    return manager != nullptr ? *reinterpret_cast<const float*>(manager + kTimeScale) : 0.0f;
}

void SkyOverhaul::TimeOfDay::Set(int minutes) {
    char line[96];
    std::snprintf(line, sizeof(line),
                  "#CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(%d, %d)",
                  minutes / 60, minutes % 60);
    DevTools::Overlay::PostLine(line);
}
