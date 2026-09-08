// The local player, and what hangs off it.
#pragma once

namespace DevTools::Player {

void Install();

// Null outside a session, which is how everything here tells a live game from a menu.
void* Local();

// The camera manager for the local player's scene, or null. Takes the player when the caller
// already has one, because most callers are on a frame that resolved it once.
void* CameraManager(void* local);
void* CameraManager();

// Whether an object sighted earlier is still the object it was. Anything written every frame must
// pass this first: a level load otherwise leaves a stale pointer writing into freed heap.
bool ObjectIsLive(void* object, void* vtable, void* local);

}
