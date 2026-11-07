// The player's graphic component: its two models, the full body first and the headless avatar
// second, and how each is drawn.
#pragma once

#include <cstdint>

namespace ThirdPerson::Graphic {

inline constexpr unsigned kFullBody = 0;
inline constexpr unsigned kAvatar = 1;

// After the engine's model switch has drawn `pawn` from inside, as the avatar, or from outside, as
// the full body. Game thread.
using ViewModeFn = void (*)(uint8_t* pawn, bool firstPerson);

// Keeps the full body through the first-person view, which the engine would discard, and calls
// `viewMode` after each model switch. False, and logged, with nothing patched, when this build lacks
// any of it.
bool Install(ViewModeFn viewMode);

uint32_t Count(uint8_t* graphic);

// Draws model `index` in every viewport and in the world's own depth range, not the near one the
// first-person gun and arms are drawn in, and keeps it out of that range until LetGo.
void KeepInWorld(uint8_t* graphic, unsigned index);
void LetGo();

void SetVisible(uint8_t* graphic, unsigned index, bool visible);

// The skeleton the models are posed on; `repose` has its bones posed again this frame.
uint8_t* Skeleton(uint8_t* graphic, bool repose);

// A model's skinning: one inverse bind matrix per skin slot, and the skeleton's bone each slot is
// skinned to.
struct Skin {
    const float* inverseBinds;
    const int32_t* bones;
    uint32_t count;
};

// False where the model is not loaded or not skinned.
bool SkinOf(uint8_t* graphic, unsigned index, Skin& out);

}
