// Raw key state, for the things the game's own input never sees.
//
// The overlay reads the keyboard through DirectInput, which the game owns and which stops delivering
// while the overlay has it. A camera mode has neither: it runs off the input pass, which is not the
// message thread, so it polls the hardware directly. That is also why Focused() exists - polled keys
// keep arriving when the window is in the background or the overlay is taking typing.
#pragma once

#include <cstddef>

namespace DevTools::Keys {

// Whether a polled key should be acted on at all: this process is in the foreground, and the
// overlay is not holding input.
bool Focused();

bool Down(int key);

// True on the frame `key` goes down. `latch` is the caller's own memory of the last frame.
bool Pressed(int key, bool& latch);

// The keys a hotkey setting may be bound to, as Choice labels. Index 0 is "Off". Home is absent:
// the overlay owns it.
const char* const* ChoiceLabels(size_t& count);

// Indices into that list, for a default binding. Checked against the real table in keys.cpp.
constexpr int kChoiceOff = 0;
constexpr int kChoiceF1 = 1;
constexpr int kChoiceF2 = 2;

// The virtual key for a Choice index, or 0 for "Off" and for an index out of range.
int FromChoice(size_t choice);

// Reports what a hotkey setting ended up bound to. `bound` is what actually took, which is 0 when
// the choice was "Off" and also when something else already held that key.
void LogBinding(const char* name, size_t choice, int bound);

}
