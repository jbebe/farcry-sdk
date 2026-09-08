// The gameplay input pass, as somewhere to run code that needs the player.
//
// GameThread::Post runs a job on the engine's frame, which is the right seam for anything that only
// needs to be between frames. This is the narrower one: the pass that reads the player's input, where
// the listener and the pawn are both in hand. Anything wanting the look accumulators, the pawn state
// block, or simply "a frame in which the player exists" belongs here rather than there.
#pragma once

#include <cstdint>

namespace DevTools::PawnTick {

// One frame of the input pass. Nothing ticks in a menu or during a load - there is no pawn to read.
//
// `local` and `focused` are resolved once here rather than by each subscriber: both were being asked
// for several times a frame, and the second answer is always the first one.
struct Frame {
    uintptr_t listener;
    uintptr_t pawn;
    float delta;
    void* local;
    bool focused;
};

using TickFn = void (*)(const Frame& frame);

// Adds a subscriber, in the order they will be called. Call before Install().
void Subscribe(TickFn fn);

// Hooks the input pass. Call once from FCSE_Load, after every Subscribe. Logs and leaves every
// subscriber inert if there is no pass to run on.
void Install();

}
