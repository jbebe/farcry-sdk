// The flashlight: a spot light at the player's head, switched by the Flashlight control, with a
// click and a HUD icon.
//
// A press plays the click at once, switches the light a moment later and eases it up to full; the
// HUD icon for the new state fades in and out again after the stock HUD's delay.
#include "flashlight.h"

#include "crc32.h"
#include "engine/frame.h"
#include "engine/hud.h"
#include "engine/input.h"
#include "engine/light.h"
#include "engine/player.h"
#include "engine/sound.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <iterator>
#include <numbers>

namespace {
    constexpr uint32_t kToggleSignal = Flashlight::Crc32("toggle_flashlight");

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

    // Where the lamp sits relative to the eye, in metres.
    constexpr float kAbove = 0.03f;
    constexpr float kRight = 0.12f;

    // Outer and inner angles in degrees, per Cone setting.
    struct Cone {
        float outer;
        float inner;
    };
    constexpr Cone kCones[] = {{30.0f, 12.0f}, {45.0f, 20.0f}, {60.0f, 30.0f}};
    static_assert(std::size(kCones) == std::size(Flashlight::kConeLabels));

    // What this layer's hud.mgb exports: the flashlight's own group, and its icon per state.
    constexpr uint32_t kHudGroup = Flashlight::Crc32("HUD_FLASHLIGHT_FADE");
    constexpr uint32_t kHudOn = Flashlight::Crc32("HUD_FLASHLIGHT_ON");
    constexpr uint32_t kHudOff = Flashlight::Crc32("HUD_FLASHLIGHT_OFF");
    // The group fades in from frame 1 and holds at 5, and fades out from 6.
    constexpr int kHudShowFrame = 1;
    constexpr int kHudHideFrame = 6;
    // Seconds the group stays up after a toggle: CFCXMainHudUI's fadeOutDelay.
    constexpr float kHudHold = 3.0f;

    constexpr float kNever = -1.0f;

    // Counted on the dispatcher, spent on the frame.
    std::atomic<int> g_toggles{0};

    // Settings, set before the first frame by their registration and on every change after.
    std::atomic<size_t> g_cone{0};
    std::atomic<bool> g_shadows{false};
    std::atomic<bool> g_reconfigure{false};

    // What the frame keeps between frames. A new player means a new world, which starts it over.
    void* g_owner = nullptr;
    bool g_on = false;
    bool g_wanted = false;
    float g_switchIn = kNever;
    float g_litFor = 0.0f;
    bool g_iconSynced = false;
    float g_hideIn = kNever;

    float Radians(float degrees) { return degrees * std::numbers::pi_v<float> / 180.0f; }

    // Counts `timer` down by `seconds`; true on the frame it runs out, which also stops it.
    bool Elapsed(float& timer, float seconds) {
        if (timer == kNever) {
            return false;
        }
        timer -= seconds;
        if (timer > 0.0f) {
            return false;
        }
        timer = kNever;
        return true;
    }

    Flashlight::Light::Spot CurrentSpot() {
        const Cone& cone = kCones[g_cone];
        return {kRange, Radians(cone.outer), Radians(cone.inner), kColour, g_shadows, kShadowFactor};
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

    void Tick(float seconds) {
        void* player = Flashlight::Player::Local();
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
        if (toggles > 0) {
            g_iconSynced = false;
            PlayGroup(kHudShowFrame);
            g_hideIn = kHudHold;
        }
        if (!g_iconSynced) {
            g_iconSynced = SyncIcon(g_wanted);
        }
        if (Elapsed(g_hideIn, seconds)) {
            PlayGroup(kHudHideFrame);
        }

        if (Elapsed(g_switchIn, seconds) && g_wanted != g_on) {
            g_on = g_wanted;
            if (g_on) {
                Flashlight::Light::Configure(CurrentSpot());
                g_litFor = 0.0f;
            }
            Flashlight::Light::SetEnabled(g_on);
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
    if (!Player::Install() || !Light::Install()) {
        return false;
    }
    Sound::Install();
    Hud::Install();
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
