// Cap the frame rate, using the engine's own limiter.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/display/maxfps.ixx.
//
// The limiter is already there and already works; nothing exposes it. `gfx_MaxFps` is the setting
// behind it - the console's own SetMaxFrameRate reads its arguments and returns, so the console is
// not a way in - and it lives at offset 0xC8 in the block the render profile global points at.
//
// Two halves, because one alone is not enough. The command line sets the rate the engine starts
// with, since it is parsed once during startup and never read again. Writing the field afterwards
// and raising the render-settings broadcast is what makes a change made from the menu take effect
// without a restart, and is the route CFCXOptionDisplayPage itself uses. Both the global and the
// broadcast are read out of the tail of that page's apply, the only place the two meet.
//
// The broadcast walks four listener lists calling a virtual on every entry, so it is raised only
// from the engine thread; off it, the field is written and the engine picks it up on its own.
#include "fcse_api.h"

#include "engine/command_line.h"

#include <windows.h>

#include <cstdint>
#include <cstdio>

namespace {
    const char* const kMaxFpsSwitch = "RenderProfile_MaxFps";

    // gfx_MaxFps, an int, in the render profile's settings block.
    constexpr ptrdiff_t kProfileMaxFps = 0xC8;

    // The limiter clamps below 1 and skips itself from 1000 up, so this is "no cap" spelled as a
    // rate nothing can reach.
    constexpr int32_t kUnlockedFps = 9999;

    // Into the tail: the disp32 of `mov ecx,[render profile]`, and the jmp to the broadcast.
    constexpr ptrdiff_t kTailProfilePointer = 9;
    constexpr ptrdiff_t kTailNotify = 16;

    // EnumDisplaySettings answers 0 or 1 for a display with no fixed rate; both mean "default".
    constexpr DWORD kUnknownRefreshRate = 1;
    constexpr int32_t kFallbackFps = 60;

    // Index order is the file format, and FCSE stores a Choice by its label. Declared in main.cpp.
    enum MaxFpsMode : uint32_t {
        kGameDefault = 0,
        kScreenRefresh = 2,
    };

    // Indexed by the choice itself, so adding a rate is one entry here and one label there. The two
    // zeroes are the modes that are not a fixed number.
    constexpr int32_t kRates[] = {0,  kUnlockedFps, 0,   30,  60,  72, 75,
                                  90, 120,          144, 165, 180, 240};

    using NotifyRenderSettingsFn = void(__fastcall*)(void* profile);

    FCSE::Relocation<uint8_t*> g_displayPageTail{
        FCSE::Pattern("8B C8 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 83 C4 0C E9")};

    void** g_renderProfile = nullptr;
    NotifyRenderSettingsFn g_notifyRenderSettings = nullptr;

    uint32_t g_mode = kGameDefault;

    int32_t DisplayRefreshRate() {
        DEVMODEW mode = {};
        mode.dmSize = sizeof(mode);

        // ENUM_CURRENT_SETTINGS is the mode the display is running now, which is what a limiter has
        // to match; the adapter's supported-mode list would give its highest rate instead.
        if (!EnumDisplaySettingsW(nullptr, ENUM_CURRENT_SETTINGS, &mode)) {
            return kFallbackFps;
        }

        if ((mode.dmFields & DM_DISPLAYFREQUENCY) == 0 ||
            mode.dmDisplayFrequency <= kUnknownRefreshRate) {
            return kFallbackFps;
        }

        // Whole hertz, so a 59.94 Hz mode arrives as 59 and the cap lands just under the refresh.
        return static_cast<int32_t>(mode.dmDisplayFrequency);
    }

    int32_t ResolveFps() {
        return g_mode == kScreenRefresh ? DisplayRefreshRate() : kRates[g_mode];
    }

    void ApplyLive() {
        if (g_renderProfile == nullptr || *g_renderProfile == nullptr) {
            return;
        }

        void* profile = *g_renderProfile;
        *reinterpret_cast<int32_t*>(static_cast<uint8_t*>(profile) + kProfileMaxFps) = ResolveFps();

        if (g_notifyRenderSettings != nullptr && UFCP::IsEngineThread()) {
            g_notifyRenderSettings(profile);
        }
    }
}

void InstallMaxFrameRateHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_displayPageTail) {
        api->Log("max frame rate: the display page's apply was not found in this build - the cap "
                 "can only be set at launch");
        return;
    }

    g_renderProfile = *reinterpret_cast<void***>(g_displayPageTail.address() + kTailProfilePointer);

    // The jmp's displacement, resolved against the instruction after it.
    uint8_t* jump = reinterpret_cast<uint8_t*>(g_displayPageTail.address() + kTailNotify);
    g_notifyRenderSettings = reinterpret_cast<NotifyRenderSettingsFn>(
        jump + 5 + *reinterpret_cast<int32_t*>(jump + 1));
}

void __cdecl OnMaxFrameRateChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    g_mode = value->asChoice;

    if (g_mode == kGameDefault) {
        UFCP::SetCommandLineSwitch(kMaxFpsSwitch, nullptr);
        api->Log("max frame rate: the game's own - not capped by UFCP");
        return;
    }

    const int32_t fps = ResolveFps();

    // Sets the rate the engine starts with; ignored once it has read the command line, which is
    // exactly when the write below starts working instead.
    char rate[16];
    std::snprintf(rate, sizeof(rate), "%d", fps);
    UFCP::SetCommandLineSwitch(kMaxFpsSwitch, rate);

    ApplyLive();

    FCSE::Logf("max frame rate: %d", fps);
}
