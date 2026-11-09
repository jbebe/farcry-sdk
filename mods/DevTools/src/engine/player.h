// The local player, and what hangs off it.
#pragma once

#include <optional>

namespace DevTools::Player {

void Install();

// Null outside a session, which is how everything here tells a live game from a menu.
void* Local();

// The local player's CPawn, or null.
void* Pawn();

// The vehicle `pawn` sits in, or null.
void* Vehicle(void* pawn);

// Points the local player's look at a yaw and a pitch, in degrees as the remote control gives them;
// his body takes the yaw from the look a frame later. False with no player. Game thread.
bool FaceLook(std::optional<float> yawDegrees, std::optional<float> pitchDegrees);

// The local player's look pitch in radians, up positive, and whether he is crouched; 0 and false with
// no player.
float LookPitch();
bool Crouched();

// The camera manager for the local player's scene, or null. Takes the player when the caller
// already has one, because most callers are on a frame that resolved it once.
void* CameraManager(void* local);
void* CameraManager();

// Whether an object sighted earlier is still the object it was. Anything written every frame must
// pass this first: a level load otherwise leaves a stale pointer writing into freed heap.
bool ObjectIsLive(void* object, void* vtable, void* local);

}
