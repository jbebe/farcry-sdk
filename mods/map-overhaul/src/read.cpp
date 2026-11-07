#include "read.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace MapOverhaul::Read {

namespace {
    using SignalFn = bool(__fastcall*)(void* dispatcher, void* unused, const uint32_t* signal, void* context);

    // The pause service's listener, which sees every action map signal before the player's does, found past
    // its first instruction, which DevTools' and Inventory Overhaul's hooks overwrite, and its next two,
    // which Flashlight's does; the two signal hashes it keeps make it unique.
    FCSE::Relocation<uint8_t*> g_signalBody{
        FCSE::Pattern("53 55 56 57 8B F9 75 ?? 83 C8 01 A3 ?? ?? ?? ?? C7 05 ?? ?? ?? ?? 4B 17 40 11 A8 02 75 ?? 83 C8 02 "
                      "A3 ?? ?? ?? ?? C7 05 ?? ?? ?? ?? 78 D9 03 A7")};
    constexpr ptrdiff_t kSignalEntry = -10;
    SignalFn g_signal = nullptr;

    constexpr uint32_t kStartShooting = FCSE::Crc32("startshooting");
    constexpr uint32_t kStopShooting = FCSE::Crc32("stopshooting");
    constexpr uint32_t kToggleIcons = FCSE::Crc32("toggleicons");
    constexpr uint32_t kDrawMonocular = FCSE::Crc32("draw_monocular");
    constexpr uint32_t kHolsterMonocular = FCSE::Crc32("holster_monocular");
    // A signal no listener knows, which one taken becomes.
    constexpr uint32_t kTaken = FCSE::Crc32("MapOverhaul.Taken");

    // The pawn's data and its skills: the first of the skills' two field-of-view transitions, the iron
    // sights' and the monocular's, which the look's speed at full zoom goes with; and the effective look's
    // yaw, left positive.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataSkills = 0xD8;
    constexpr ptrdiff_t kZoom = 0x08;
    constexpr ptrdiff_t kZoomOn = 0x05;
    constexpr ptrdiff_t kZoomTime = 0x08;
    constexpr ptrdiff_t kZoomFov = 0x14;
    constexpr ptrdiff_t kZoomCurve = 0x18;
    constexpr ptrdiff_t kSkillsZoomedLookSpeed = 0x44;
    constexpr ptrdiff_t kDataYaw = 0x2D0 + 0x40;

    // The field of view read at, radians: three times closer than an 87 degree view. How long it takes to
    // come in and go, the look's speed there, about the retail 0.3 a radian of view, and how long the sheet
    // takes to swing back in front once let go.
    constexpr float kFov = 0.61f;
    constexpr float kTime = 0.3f;
    constexpr float kLookSpeed = 0.19f;
    constexpr float kLetGo = 0.3f;
    // How far the look may turn off the sheet's middle before it brings the sheet along, radians: the
    // sheet's edge at the middle of the view.
    constexpr float kReach = 0.6f;
    constexpr float kPi = 3.14159265f;

    // Whether the map is out, left mouse held and the monocular up, as the signals and the map last said.
    std::atomic<bool> g_out{false};
    std::atomic<bool> g_pressed{false};
    std::atomic<bool> g_monocular{false};
    // On the game thread: the pawn zoomed, the look's yaw when the zoom came in, how far the sheet is turned
    // back against it, and that turn and how long ago when it was let go.
    uint8_t* g_zoomed = nullptr;
    float g_fromYaw = 0.0f;
    float g_turn = 0.0f;
    float g_letGoTurn = 0.0f;
    float g_letGoAge = 0.0f;

    template <class T>
    T& Field(void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<uint8_t*>(object) + offset);
    }

    bool __fastcall OnSignal(void* dispatcher, void* unused, const uint32_t* signal, void* context) {
        if (signal != nullptr) {
            uint32_t& id = *const_cast<uint32_t*>(signal);
            switch (id) {
            case kStartShooting:
                g_pressed = g_out.load();
                break;
            case kStopShooting:
                g_pressed = false;
                break;
            case kToggleIcons:
                if (g_out) {
                    id = kTaken;
                }
                break;
            case kDrawMonocular:
                g_monocular = true;
                break;
            case kHolsterMonocular:
                g_monocular = false;
                break;
            }
        }
        return g_signal(dispatcher, unused, signal, context);
    }

    // Each gun and the monocular write their own transition and look speed as they come out, and the map
    // puts the gun away, so nothing written here outlasts the map.
    void Zoom(uint8_t* pawn, bool on) {
        uint8_t* skills = Field<uint8_t*>(pawn, kPawnData) + kDataSkills;
        if (on) {
            Field<float>(skills, kZoom + kZoomTime) = kTime;
            Field<float>(skills, kZoom + kZoomFov) = kFov;
            Field<int32_t>(skills, kZoom + kZoomCurve) = -1;
            Field<float>(skills, kSkillsZoomedLookSpeed) = kLookSpeed;
        }
        Field<uint8_t>(skills, kZoom + kZoomOn) = on ? 1 : 0;
    }

    float Wrap(float angle) {
        return std::remainder(angle, 2.0f * kPi);
    }
}

bool Install() {
    const bool hooked = g_signalBody && FCSE::ApiPointer()->Hook(g_signalBody.get() + kSignalEntry,
                                                                 reinterpret_cast<void*>(&OnSignal),
                                                                 reinterpret_cast<void**>(&g_signal));
    FCSE::Logf("read: %s", hooked ? "left mouse held reads the sheet close" : "off, the signals were not found");
    return hooked;
}

float Update(uint8_t* pawn, float seconds) {
    g_out = pawn != nullptr;
    const bool wanted = pawn != nullptr && g_pressed && !g_monocular;
    if (wanted && g_zoomed == nullptr) {
        Zoom(pawn, true);
        g_zoomed = pawn;
        g_fromYaw = Field<float>(Field<uint8_t*>(pawn, kPawnData), kDataYaw);
    } else if (!wanted && g_zoomed != nullptr) {
        // The monocular, come up, takes the zoom over and lets it go itself.
        if (!g_monocular) {
            Zoom(g_zoomed, false);
        }
        g_zoomed = nullptr;
        g_letGoTurn = g_turn;
        g_letGoAge = 0.0f;
    }
    if (g_zoomed != nullptr) {
        const float yaw = Field<float>(Field<uint8_t*>(g_zoomed, kPawnData), kDataYaw);
        g_turn = std::clamp(Wrap(yaw - g_fromYaw), -kReach, kReach);
        g_fromYaw = Wrap(yaw - g_turn);
    } else {
        g_letGoAge += seconds;
        g_turn = g_letGoTurn * std::max(0.0f, 1.0f - g_letGoAge / kLetGo);
    }
    return g_turn;
}

}
