// Solid axles: the two wheels of a beam axle sprung, damped and drawn as one.
//
// The beam rides on two springs closer together than its wheels. Both wheels rising alike meet the
// springs as two wheels sprung apart would; one rising and the other falling twist the beam, which the
// springs resist only from where they sit, so the axle twists under the body and keeps both wheels down.
#include "axles.h"

#include <algorithm>
#include <cmath>

namespace {
    // How much further than its spring reaches a wheel on a solid axle can hang, as a share of the
    // spring's length.
    constexpr float kDroop = 0.5f;
    // Over this last share of its droop, a hanging wheel takes less and less of the far spring's push,
    // as the axle comes to the end of its twist.
    constexpr float kDroopEnd = 0.25f;
}

namespace VehicleOverhaul::Axles {

void Hang(const Wheeled::Parts& parts, const RealVehicle::Spec& real) {
    for (int i = 0; i < parts.wheels; ++i) {
        if (real.solidAxle[Wheeled::AxleOf(i)] > 0.0f) {
            *parts.wheel[i].suspensionLength *= 1.0f + kDroop;
        }
    }
}

void Spring(float mass, const RealVehicle::Spec& real, const Wheeled::Suspension* wheels, float* forces) {
    for (int first = 0; first < Wheeled::kMaxWheels; first += 2) {
        const float spacing = real.solidAxle[Wheeled::AxleOf(first)];
        if (spacing <= 0.0f) {
            continue;
        }
        const Wheeled::Suspension* wheel = wheels + first;

        // How far each wheel has pushed its spring in, negative while it hangs past where the spring stops
        // pushing, and how much of the far spring's push it takes at the end of its droop.
        float compression[2];
        float farShare[2];
        for (int side = 0; side < 2; ++side) {
            const float rest = wheel[side].reach / (1.0f + kDroop);
            compression[side] = rest - wheel[side].length;
            farShare[side] = std::clamp(
                (wheel[side].reach - wheel[side].length) / (kDroopEnd * (wheel[side].reach - rest)), 0.0f, 1.0f);
        }
        const float heave = (compression[0] + compression[1]) / 2.0f;
        const float twist = (compression[0] - compression[1]) / 2.0f;
        const float heaveSpeed = (wheel[0].extending + wheel[1].extending) / 2.0f;
        const float twistSpeed = (wheel[0].extending - wheel[1].extending) / 2.0f;

        // The push of each side's spring and damper, which sit `spacing` of the way out from the axle's
        // middle to its wheel.
        float spring[2];
        float damper[2];
        for (int side = 0; side < 2; ++side) {
            const float out = side == 0 ? spacing : -spacing;
            const float extending = heaveSpeed + out * twistSpeed;
            spring[side] = wheel[side].strength * (std::max)(0.0f, heave + out * twist);
            damper[side] =
                -(extending < 0.0f ? wheel[side].compressionDamping : wheel[side].reboundDamping) * extending;
        }

        // The beam hands each side's push to both wheels, the more to the nearer. A wheel in the air keeps
        // Havok's none.
        const float nearer = (1.0f + spacing) / 2.0f;
        for (int side = 0; side < 2; ++side) {
            if (!wheel[side].touching) {
                continue;
            }
            const int other = 1 - side;
            const float across = (1.0f - spacing) / 2.0f * farShare[side];
            forces[first + side] = (wheel[side].slant * (nearer * spring[side] + across * spring[other]) +
                                    nearer * damper[side] + across * damper[other]) *
                                   mass;
        }
    }
}

void Lean(Wheeled::Car car, const RealVehicle::Spec* real) {
    float angles[Wheeled::kAxles] = {};
    for (int first = 0; real != nullptr && first < Wheeled::kMaxWheels; first += 2) {
        if (real->solidAxle[Wheeled::AxleOf(first)] > 0.0f) {
            float a[3];
            float b[3];
            Wheeled::WheelCentre(car, first, a);
            Wheeled::WheelCentre(car, first + 1, b);
            angles[Wheeled::AxleOf(first)] = std::atan((a[2] - b[2]) / (b[0] - a[0]));
        }
    }
    Wheeled::LeanWheels(angles);
}

}
