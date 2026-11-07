// A body's skeleton as the engine poses it: its bones, and each time they are posed anew.
// See docs/docs/engine-internals/first-person-aiming.md#the-bodys-skeleton.
#pragma once

#include <cstdint>

namespace ThirdPerson::Skeleton {

uint32_t Count(uint8_t* skeleton);

// The bones' array, which a model's skinning is handed.
uint8_t* Bones(uint8_t* skeleton);

int Parent(uint8_t* skeleton, uint32_t index);

// A bone's posed 4x4 in the body's own axes, x right, y ahead, z up, whose first three rows are the
// bone's axes and the fourth its place.
float* Posed(uint8_t* skeleton, uint32_t index);

// The index of the bone whose name has this CRC-32, or -1.
int Find(uint8_t* skeleton, uint32_t name);

// The posed matrix in an array of bones, as a model's skinning is handed them.
const float* PosedIn(const uint8_t* bones, int32_t index);

// Runs after the engine poses a skeleton's bones anew, on whichever thread posed them.
using PosedFn = void (*)(uint8_t* skeleton);

// False, and logged, when posing cannot be hooked.
bool Install(PosedFn posed);

}
