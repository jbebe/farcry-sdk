// The player's pawn: once a frame on the game thread, and what it is doing.
#pragma once

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace PlayerBody::Pawn {

// CPawn's data; in it the body's graphic component, and the effective state with its flags, whose
// 0x80 is the first-person body, and its look, at the second place while the flags have 0x04.
inline constexpr ptrdiff_t kData = 0x10;
inline constexpr ptrdiff_t kGraphic = 0x0C;
inline constexpr ptrdiff_t kEffective = 0x2D0;
inline constexpr ptrdiff_t kEffectiveFlags = kEffective + 0x04;
inline constexpr uint8_t kFirstPerson = 0x80;
inline constexpr ptrdiff_t kLook = 0x38;
inline constexpr ptrdiff_t kOtherLook = 0x54;
inline constexpr uint8_t kOtherLookFlag = 0x04;
// Also in it, whether a jump has left the ground and whether it has ended, and how far the pawn is
// above the ground, in metres.
inline constexpr ptrdiff_t kJumping = 0x49B;
inline constexpr ptrdiff_t kJumpEnded = 0x49C;
inline constexpr ptrdiff_t kAboveGround = 0x4A4;

// An entity's 4x4 in the world: three axes, x right, y ahead and z up, then its place.
inline constexpr ptrdiff_t kEntityMatrix = 0x30;

// Runs once a frame on the game thread while a player exists.
using TickFn = void (*)(uint8_t* pawn);

// Hooks the gameplay input pass. False, and logged, when it or the seat lookup is not found.
bool Install(TickFn tick);

inline uint8_t* Data(uint8_t* pawn) {
    return Field<uint8_t*>(pawn, kData);
}

inline uint8_t* Graphic(uint8_t* pawn) {
    return Field<uint8_t*>(Data(pawn), kGraphic);
}

inline bool FirstPerson(uint8_t* pawn) {
    return (Field<uint8_t>(Data(pawn), kEffectiveFlags) & kFirstPerson) != 0;
}

// Pitch, roll and yaw in radians, as the third-person camera reads them: pitch up positive, yaw 0
// facing +Y and turning left.
inline const float* Look(uint8_t* pawn) {
    const bool other = (Field<uint8_t>(Data(pawn), kEffectiveFlags) & kOtherLookFlag) != 0;
    return &Field<float>(Data(pawn), kEffective + (other ? kOtherLook : kLook));
}

inline bool Jumping(uint8_t* pawn) {
    return Field<uint8_t>(Data(pawn), kJumping) != 0 && Field<uint8_t>(Data(pawn), kJumpEnded) == 0;
}

inline float AboveGround(uint8_t* pawn) {
    return Field<float>(Data(pawn), kAboveGround);
}

// The entity of the pawn, or of any other entity component, or null.
uint8_t* Entity(uint8_t* component);

// Where an entity stands in the world, three floats.
inline const float* Place(uint8_t* entity) {
    return &Field<float>(entity, kEntityMatrix) + 12;
}

// Whether the pawn sits in no vehicle.
bool OnFoot(uint8_t* pawn);

// Draws `pawn` from inside, as the avatar, or from outside, as the full body with its shadow, the
// weapons it carries and the animations for that view, as CPawn::ViewModeChanged does.
void SetFirstPerson(uint8_t* pawn, bool firstPerson);

}
