// Leaning: held Q or E slides the upper body out to the side and bends it from the belly, and the
// head, the view, the arms and the gun go with it; see
// docs/docs/engine-internals/first-person-aiming.md#leaning.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// A bend of the body: radians, right positive, about the look's direction, which is pitched up by
// `pitch` radians.
struct Bend {
    float angle;
    float pitch;

    bool operator==(const Bend&) const = default;
};

// Follows the lean signals: a lean key tapped leans until it is tapped again, and held, for as long
// as it is held.
void OnSignal(uint32_t signal);

// Leans the pawn's body this frame, given the seconds since the last, whether the player may lean
// now and whether the body can be bent at all. Returns how far the view rolls with it, in radians,
// right positive.
float Update(uint8_t* pawn, float seconds, bool free, bool posable);

// The bend the body is posed with this frame.
Bend Posed();

// Bends a posed matrix about `pivot`, the spine's unbent place, and slides it.
void Apply(float* model, const float pivot[3], Bend bend);

// How far the upper body slides to the side, in metres, right positive, as it bends by `angle`.
float SlideFor(float angle);

}
