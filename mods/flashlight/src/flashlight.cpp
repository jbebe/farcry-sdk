// The flashlight: a spot light at the player's head, switched by the Flashlight control, with a
// click and a HUD icon.
//
// A press plays the click at once and switches the light a moment later, so the two land together;
// the light then eases up to full like a bulb. The HUD icon for the new state fades in on its own
// group and out again after the stock HUD's delay.
#include "flashlight.h"

#include "engine/frame.h"
#include "engine/hud.h"
#include "engine/input.h"
#include "engine/light.h"
#include "engine/sound.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <numbers>

namespace {
    // CRC32 of the signal the toggle_flashlight control sends.
    constexpr uint32_t kToggleSignal = 0x78D9863A;

    // The switch, a bank of this layer's own, played through the player's own foley.
    constexpr uint32_t kClick = 0x00FC0A00;
    constexpr int32_t kFoleyPlayer = 15;

    // Seconds from the click to the light switching, and for the light to come up to full.
    constexpr float kSwitchDelay = 0.2f;
    constexpr float kFadeIn = 0.1f;

    constexpr float kRange = 25.0f;
    constexpr float kIntensity = 3.0f;
    constexpr float kShadowFactor = 1.0f;
    // The player headlight's warm white.
    constexpr float kColour[] = {1.0f, 0.957f, 0.820f};

    // Where the lamp sits relative to the eye, in metres: a little above and to the right, so what
    // it lights shows some shape rather than lying flat under a light at the viewpoint.
    constexpr float kAbove = 0.03f;
    constexpr float kRight = 0.12f;

    // Outer and inner angles in degrees, per Cone setting.
    struct Cone {
        float outer;
        float inner;
    };
    constexpr Cone kCones[] = {{30.0f, 12.0f}, {45.0f, 20.0f}, {60.0f, 30.0f}};
    static_assert(std::size(kCones) == std::size(Flashlight::kConeLabels));

    // The names this layer's hud.mgb exports: the flashlight's own group, and its icon per state.
    constexpr const char* kHudGroup = "HUD_FLASHLIGHT_FADE";
    constexpr const char* kHudOn = "HUD_FLASHLIGHT_ON";
    constexpr const char* kHudOff = "HUD_FLASHLIGHT_OFF";
    // The group fades in from frame 1 and holds at 5, and fades out from 6.
    constexpr int kHudShowFrame = 1;
    constexpr int kHudHideFrame = 6;
    // Seconds the group stays up after a toggle: CFCXMainHudUI's fadeOutDelay.
    constexpr float kHudHold = 3.0f;

    constexpr float kNever = -1.0f;

    // Counted on the dispatcher, spent on the frame.
    std::atomic<int> g_toggles{0};

    // Settings, written whenever the player changes them and read on the frame.
    std::atomic<size_t> g_cone{Flashlight::kDefaultCone};
    std::atomic<bool> g_shadows{true};
    std::atomic<bool> g_reconfigure{false};

    bool g_hudReady = false;

    // What the frame keeps between frames. A new player means a new world, which starts it over.
    void* g_owner = nullptr;
    bool g_on = false;
    bool g_wanted = false;
    float g_switchIn = kNever;
    float g_litFor = 0.0f;
    bool g_iconSynced = false;
    float g_hideIn = kNever;

    float Radians(float degrees) { return degrees * std::numbers::pi_v<float> / 180.0f; }

    Flashlight::Light::Spot CurrentSpot() {
        const Cone& cone = kCones[g_cone.load()];
        return {kRange, Radians(cone.outer), Radians(cone.inner), {kColour[0], kColour[1], kColour[2]},
                g_shadows.load(), kShadowFactor};
    }

    void Forget() {
        Flashlight::Light::Destroy();
        g_on = false;
        g_wanted = false;
        g_switchIn = kNever;
        g_iconSynced = false;
        g_hideIn = kNever;
    }

    // Shows the icon for `lit` whenever the group is up. False while no HUD is loaded.
    bool SyncIcon(bool lit) {
        void* on = Flashlight::Hud::Find(kHudOn);
        void* off = Flashlight::Hud::Find(kHudOff);
        if (on == nullptr || off == nullptr) {
            return false;
        }
        Flashlight::Hud::SetVisible(on, lit);
        Flashlight::Hud::SetVisible(off, !lit);
        return true;
    }

    void PlayGroup(int frame) {
        if (void* group = Flashlight::Hud::Find(kHudGroup)) {
            Flashlight::Hud::PlayFrom(group, frame);
        }
    }

    void UpdateHud(int toggles, float seconds) {
        if (toggles > 0) {
            g_iconSynced = false;
            PlayGroup(kHudShowFrame);
            g_hideIn = kHudHold;
        }
        if (!g_iconSynced) {
            g_iconSynced = SyncIcon(g_wanted);
        }
        if (g_hideIn != kNever) {
            g_hideIn -= seconds;
            if (g_hideIn <= 0.0f) {
                g_hideIn = kNever;
                PlayGroup(kHudHideFrame);
            }
        }
    }

    void Tick(float seconds) {
        void* player = Flashlight::Light::LocalPlayer();
        if (player != g_owner) {
            Forget();
            g_owner = player;
        }
        if (player == nullptr) {
            g_toggles = 0;
            return;
        }
        Flashlight::Sound::Hold(kClick);

        const int toggles = g_toggles.exchange(0);
        for (int i = 0; i < toggles; ++i) {
            g_wanted = !g_wanted;
            Flashlight::Sound::Play(kClick, kFoleyPlayer);
            g_switchIn = kSwitchDelay;
        }
        if (g_hudReady) {
            UpdateHud(toggles, seconds);
        }

        if (g_switchIn != kNever) {
            g_switchIn -= seconds;
            if (g_switchIn <= 0.0f) {
                g_switchIn = kNever;
                if (g_wanted != g_on) {
                    g_on = g_wanted;
                    if (g_on) {
                        Flashlight::Light::Configure(CurrentSpot());
                        g_litFor = 0.0f;
                    }
                    Flashlight::Light::SetEnabled(g_on);
                }
            }
        }

        if (g_reconfigure.exchange(false) && g_on) {
            Flashlight::Light::Configure(CurrentSpot());
        }

        if (g_on) {
            g_litFor = std::min(g_litFor + seconds, kFadeIn);
            const float t = g_litFor / kFadeIn;
            Flashlight::Light::Place(player, kIntensity * (1.0f - (1.0f - t) * (1.0f - t)), kAbove, kRight);
        }
    }

    void OnSignal(uint32_t signal) {
        if (signal == kToggleSignal) {
            ++g_toggles;
        }
    }
}

namespace Flashlight {

bool Install() {
    if (!Light::Install()) {
        return false;
    }
    Sound::Install();
    g_hudReady = Hud::Install();
    return Frame::Install(&Tick) && Input::Install(&OnSignal);
}

void SetCone(size_t cone) {
    g_cone = std::min(cone, std::size(kCones) - 1);
    g_reconfigure = true;
}

void SetShadows(bool shadows) {
    g_shadows = shadows;
    g_reconfigure = true;
}

}
