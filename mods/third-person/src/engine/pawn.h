// The player's pawn: once a frame on the game thread, and what it is doing.
#pragma once

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace ThirdPerson::Pawn {

// CPawn's data; in it the body's graphic component, the flags the pawn is asked for, and its
// effective state with its own flags and its look, at the second place while the flags have 0x04.
// In both sets of flags 0x80 is the first-person body, 0x08 the iron sights, 0x01 the gun held up
// and 0x02 the look followed without it.
inline constexpr ptrdiff_t kData = 0x10;
inline constexpr ptrdiff_t kGraphic = 0x0C;
inline constexpr ptrdiff_t kDesiredFlags = 0x144;
inline constexpr ptrdiff_t kEffective = 0x2D0;
inline constexpr ptrdiff_t kEffectiveFlags = kEffective + 0x04;
inline constexpr uint8_t kFirstPerson = 0x80;
inline constexpr uint8_t kIronsight = 0x08;
inline constexpr uint8_t kGunUp = 0x01;
inline constexpr uint8_t kLookOnly = 0x02;
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

// Runs once a frame on the game thread while a player exists. `look` is the frame's mouse look on
// the input listener, pitch then yaw in half-turns a second; zeroing it keeps the pawn's look still.
using TickFn = void (*)(uint8_t* pawn, float* look);

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

// Lets go of the iron sights the player asked for, as releasing the aim key does.
inline void DropSights(uint8_t* pawn) {
    Field<uint8_t>(Data(pawn), kDesiredFlags) &= ~kIronsight;
}

// Has the full body hold its gun up where the player looks, or carry it down and only look there.
inline void SetGunUp(uint8_t* pawn, bool up) {
    uint8_t& flags = Field<uint8_t>(Data(pawn), kDesiredFlags);
    flags = up ? (flags | kGunUp) & ~kLookOnly : (flags & ~kGunUp) | kLookOnly;
}

inline bool Jumping(uint8_t* pawn) {
    return Field<uint8_t>(Data(pawn), kJumping) != 0 && Field<uint8_t>(Data(pawn), kJumpEnded) == 0;
}

inline float AboveGround(uint8_t* pawn) {
    return Field<float>(Data(pawn), kAboveGround);
}

// The entity of the pawn, or of any other entity component, or null.
uint8_t* Entity(uint8_t* component);

// Waits for an entity's pending job, as the engine does before changing it.
void Lock(uint8_t* entity);

inline const float* Matrix(uint8_t* entity) {
    return &Field<float>(entity, kEntityMatrix);
}

// Where an entity stands in the world, three floats.
inline const float* Place(uint8_t* entity) {
    return Matrix(entity) + 12;
}

// The CVehicle component of the vehicle the pawn sits in, or null on foot.
uint8_t* Vehicle(uint8_t* pawn);

inline bool OnFoot(uint8_t* pawn) {
    return Vehicle(pawn) == nullptr;
}

// The pawn's CGadget of `category`, Crc32 of its name such as "map", and the one it has equipped;
// null for none.
uint8_t* Gadget(uint8_t* pawn, uint32_t category);
uint8_t* EquippedGadget(uint8_t* pawn);

// Hides a carried gadget as putting it away does.
void HideGadget(uint8_t* gadget);

// Draws `pawn` from inside, as the avatar, or from outside, as the full body with its shadow, the
// weapons it carries and the animations for that view, as CPawn::ViewModeChanged does.
void SetFirstPerson(uint8_t* pawn, bool firstPerson);

}
