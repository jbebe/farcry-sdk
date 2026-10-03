// The game's sound system (CSoundSystem): every sound it starts, and starting and stopping one.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::SoundSystem {

// No sound, and the handle of one that did not start.
constexpr uint32_t kNone = 0xFFFFFFFF;

// Before the game starts sound `id` of sound type `type` for `callbacks`, the object it plays for:
// false keeps it from starting.
using StartFn = bool (*)(uint32_t id, int32_t type, const void* callbacks);

// False, and logged, when this build lacks PlaySound.
bool Install(StartFn start);

// The handle, or kNone.
uint32_t Play(uint32_t id, int32_t type, void* callbacks);
void Stop(uint32_t handle);

// The game's sound types (Ambiance_Generic to Ambiance_Followers).
constexpr int32_t kTypeCount = 25;

// Given a sound type and the volume in dB the game's mix sends it, the volume to apply instead.
using TypeVolumeFn = float (*)(int32_t type, float volume);

// False, and logged, when this build lacks SetTypeVolume.
bool AdjustTypeVolumes(TypeVolumeFn adjust);

// Applies every type's last volume from the mix again, through the adjustment. Game thread.
void ResendTypeVolumes();

}
