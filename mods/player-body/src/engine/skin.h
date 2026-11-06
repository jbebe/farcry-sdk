// A model's skin matrices, which its vertices are drawn with.
#pragma once

#include <cstdint>

namespace PlayerBody::Skin {

// After a model's skin matrices are built, on the skinning job's thread: one 4x4 per slot in
// `out`, slot i from the posed matrix of bone `slotBones[i]` in `bones` and the model's
// `inverseBinds`, which tell the models apart.
using SkinnedFn = void (*)(float* out, const uint8_t* bones, const float* inverseBinds,
                           const int32_t* slotBones, uint32_t count);

// False, and logged, when the skinning cannot be hooked.
bool Install(SkinnedFn skinned);

}
