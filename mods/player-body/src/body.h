// The player's two models in first person on foot: the full body drawn in the world with only its
// hips and legs, and the avatar, drawn as the game draws it, with everything but them. Seen from a
// camera away from the eye, the full body is drawn whole and the avatar not at all.
#pragma once

#include <cstdint>

namespace PlayerBody::Body {

void SetEnabled(bool enabled);

// Once a frame on the game thread. True while the full body is drawn, and so its legs posed.
bool Follow(uint8_t* pawn);

// Where the eye of `pawn` is in the world: its skeleton's camera bone. False while that is not
// loaded.
bool Eye(uint8_t* pawn, float out[3]);

// After the engine has set the view mode of `pawn`, which undoes what Follow set.
void OnViewMode(uint8_t* pawn, bool firstPerson);

// Collapses the bones a model does not show to a point, on the skinning job's thread.
void OnSkinned(float* out, const uint8_t* bones, const float* inverseBinds,
               const int32_t* slotBones, uint32_t count);

}
