// Leaning: held Q or E slides the upper body out to the side and bends it from the belly, and the
// head, the view, the arms and the gun go with it; see
// docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "lean.h"

#include "ease.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <windows.h>

#include <cmath>

namespace {
    constexpr uint32_t kStartLeft = FCSE::Crc32("start_lean_left");
    constexpr uint32_t kStopLeft = FCSE::Crc32("stop_lean_left");
    constexpr uint32_t kStartRight = FCSE::Crc32("start_lean_right");
    constexpr uint32_t kStopRight = FCSE::Crc32("stop_lean_right");

    // Radians and metres to the side at full lean, the upper body sliding over the hips as it
    // bends, and seconds for the lean to follow, which takes it most of the way in a quarter of a
    // second.
    constexpr float kMostBend = 0.26f;
    constexpr float kMostSlide = 0.10f;
    constexpr float kFollow = 0.08f;

    // A lean key let go within this many milliseconds was tapped, and the lean stays until the next
    // tap; held longer, the lean ends with it.
    constexpr ULONGLONG kTapMilliseconds = 250;

    // The side leaned to, -1 left, 1 right, 0 none; whether the lean outlasts its key, tapped; the
    // side whose press just ended a tapped lean, whose release is not a tap; and when the last lean
    // began.
    int g_side = 0;
    bool g_latched = false;
    int g_ended = 0;
    ULONGLONG g_pressed = 0;
    float g_bend = 0.0f;

    // The bend that posing the body applies, in radians, and the look's pitch it bends about.
    float g_posedBend = 0.0f;
    float g_posedPitch = 0.0f;

    void Press(int side) {
        if (g_side == side && g_latched) {
            g_side = 0;
            g_latched = false;
            g_ended = side;
            return;
        }
        g_side = side;
        g_latched = false;
        g_pressed = GetTickCount64();
    }

    void Release(int side) {
        if (g_ended == side) {
            g_ended = 0;
        } else if (g_side == side) {
            g_latched = GetTickCount64() - g_pressed < kTapMilliseconds;
            if (!g_latched) {
                g_side = 0;
            }
        }
    }

    // A turn about the look's direction, ahead pitched up by the bend's pitch. The view rolls about
    // that same direction, so whatever hangs from it stays lined up on it.
    struct Turn {
        float u[3];
        float c;
        float s;

        explicit Turn(AimingOverhaul::Lean::Bend bend)
            : u{0.0f, std::cos(bend.pitch), std::sin(bend.pitch)}, c(std::cos(bend.angle)),
              s(std::sin(bend.angle)) {}

        // Turns the point or axis `v`, in the body's axes.
        void operator()(float* v) const {
            const float along = (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) * (1.0f - c);
            const float cross[3] = {u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2],
                                    u[0] * v[1] - u[1] * v[0]};
            for (int k = 0; k < 3; ++k) {
                v[k] = v[k] * c + cross[k] * s + u[k] * along;
            }
        }
    };
}

void AimingOverhaul::Lean::OnSignal(uint32_t signal) {
    if (signal == kStartLeft) {
        Press(-1);
    } else if (signal == kStopLeft) {
        Release(-1);
    } else if (signal == kStartRight) {
        Press(1);
    } else if (signal == kStopRight) {
        Release(1);
    }
}

float AimingOverhaul::Lean::Update(uint8_t* pawn, float seconds, bool free, bool posable) {
    // Sprinting ends a tapped lean, rather than leaving it to come back after.
    if (g_latched && !free) {
        g_side = 0;
        g_latched = false;
    }
    float target = 0.0f;
    if (free) {
        target = kMostBend * static_cast<float>(g_side);
    }
    g_bend = Follow(g_bend, target, seconds, kFollow);
    if (target == 0.0f && std::fabs(g_bend) < 1e-4f) {
        g_bend = 0.0f;
    }
    if (!posable) {
        g_posedBend = 0.0f;
        return 0.0f;
    }
    // The view rolls with the bend the skeleton was last posed with, which is what will be drawn.
    const float bend = g_posedBend;
    g_posedBend = g_bend;
    g_posedPitch = Pawn::Pitch(pawn);
    return bend;
}

AimingOverhaul::Lean::Bend AimingOverhaul::Lean::Posed() {
    // The pitch matters only to a bend.
    return g_posedBend != 0.0f ? Bend{g_posedBend, g_posedPitch} : Bend{};
}

void AimingOverhaul::Lean::Apply(float* model, const float pivot[3], Bend bend) {
    const Turn turn(bend);
    turn(model);
    turn(model + 4);
    turn(model + 8);
    float* place = model + 12;
    for (int k = 0; k < 3; ++k) {
        place[k] -= pivot[k];
    }
    turn(place);
    for (int k = 0; k < 3; ++k) {
        place[k] += pivot[k];
    }
    place[0] += SlideFor(bend.angle);
}

float AimingOverhaul::Lean::SlideFor(float angle) {
    return angle / kMostBend * kMostSlide;
}
