// The player's first-person body as the engine poses it each frame: the upper body bent by the lean,
// and the arms and the gun held low in a sprint or swung out of a wall, while the view's bone stays
// where the animation holds it.
// See docs/docs/engine-internals/first-person-aiming.md#the-bodys-skeleton.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Body {

// The arms and the gun off where the animation holds them, in the view's own axes: radians turned
// left, up and rolled right, about a point below and behind the eye, then metres moved.
struct Offset {
    float yaw;
    float pitch;
    float roll;
    float right;
    float ahead;
    float up;

    bool operator==(const Offset&) const = default;
};

inline Offset operator+(const Offset& a, const Offset& b) {
    return {a.yaw + b.yaw,     a.pitch + b.pitch, a.roll + b.roll,
            a.right + b.right, a.ahead + b.ahead, a.up + b.up};
}

// Once a frame on the game thread, after the lean and the arms are worked out: follows the pawn's
// skeleton, having its bones posed again when the pose changed, and hands the pose to the posing
// threads.
void Follow(uint8_t* pawn);

// Whether the last skeleton followed can be posed.
bool Found();

// Bends and moves a skeleton freshly posed, if it is the player's.
void OnPosed(uint8_t* skeleton);

// Bends what is placed from the player's view bone, though the bone itself stays: the camera, and a
// long gun at the sights, which the arms move with too. True where it changed the copy.
bool OnAttached(uint8_t* skeleton, int bone, uint8_t* child, float* model);

// Moves the player's shot, leaving from `origin` in the world, to where the lean has taken the view's
// bone.
void MoveShot(uint8_t* pawn, float origin[3]);

}
