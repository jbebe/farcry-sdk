// A body's skeleton as the engine poses it: its bones, and each time they are posed anew.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Skeleton {

// One posed bone: its 4x4 in the body's own axes, x right, y ahead, z up, whose first three rows
// are the bone's axes and the fourth its place; its parent's index, below nought for none; and
// whether the engine set it by hand, so that posing leaves it as it is.
struct Bone {
    float* model;
    int parent;
    bool held;
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

// False, and logged, when posing cannot be hooked.
bool Install(PosedFn posed);

}
