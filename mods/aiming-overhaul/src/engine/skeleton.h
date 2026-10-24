// A body's skeleton as the engine poses it: its bones, and each time they are posed anew.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Skeleton {

// One posed bone: its 4x4 in the body's own axes, x right, y ahead, z up, whose first three rows
// are the bone's axes and the fourth its place; and its parent's index, below nought for none.
struct Bone {
    float* model;
    int parent;
};

// The skeleton the pawn's first-person body is posed on, which the camera turns with; or null.
// `repose` has its bones posed again this frame though the animation has not moved them.
uint8_t* Of(uint8_t* pawn, bool repose);

uint32_t Count(uint8_t* skeleton);
Bone At(uint8_t* skeleton, uint32_t index);

// The index of the bone whose name has this CRC-32, or -1.
int Find(uint8_t* skeleton, uint32_t name);

// Runs after the engine poses a skeleton's bones anew from their animated ones, on whichever thread
// posed them.
using PosedFn = void (*)(uint8_t* skeleton);

// Runs as an entity bound to one of a skeleton's bones, such as a weapon in hand, is placed from
// it, with that bone's posed matrix as copied for the entity, which the entity is placed from.
using AttachedFn = void (*)(uint8_t* skeleton, int bone, float* model);

// False, and logged, when posing cannot be hooked.
bool Install(PosedFn posed, AttachedFn attached);

}
