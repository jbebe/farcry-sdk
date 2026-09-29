// CSoundSystem, the game's side of DARE: every sound the game plays goes through its vtable.
#pragma once

#include <cstddef>

namespace SoundOverhaul::SoundSystem {

constexpr size_t kGetListenerCallbacks = 0x0C / sizeof(void*);
constexpr size_t kUpdate = 0x50 / sizeof(void*);
constexpr size_t kStopAllSoundsOfType = 0x78 / sizeof(void*);
constexpr size_t kPlaySoundReverb = 0x98 / sizeof(void*);
constexpr size_t kPlaySound = 0x9C / sizeof(void*);
constexpr size_t kPlaySoundAtPosition = 0xA0 / sizeof(void*);
constexpr size_t kPlaySoundStayAlive = 0xA4 / sizeof(void*);
constexpr size_t kStopSound = 0xA8 / sizeof(void*);
// MSVC orders the two StopAllSounds overloads the reverse of the server build.
constexpr size_t kStopAllSounds = 0xB4 / sizeof(void*);
constexpr size_t kStopAllSoundsOf = 0xB8 / sizeof(void*);
constexpr size_t kIsSoundPlaying = 0xC4 / sizeof(void*);

// The vtable, read from the constructor; null when this build's constructor was not found.
void** Vtable();

}
