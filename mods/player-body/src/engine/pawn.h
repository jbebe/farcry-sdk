// The player's pawn: once a frame on the game thread, and what it is doing.
#pragma once

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace PlayerBody::Pawn {

// CPawn's data; in it the body's graphic component, and the effective flags, whose 0x80 is the
// first-person body.
inline constexpr ptrdiff_t kData = 0x10;
inline constexpr ptrdiff_t kGraphic = 0x0C;
inline constexpr ptrdiff_t kEffectiveFlags = 0x2D4;
inline constexpr uint8_t kFirstPerson = 0x80;
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

}
