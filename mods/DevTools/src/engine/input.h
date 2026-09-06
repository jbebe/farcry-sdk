// The game's keyboard and mouse, borrowable.
//
// Far Cry 2 reads the two of them twice over: window messages for anything textual, and DirectInput
// for everything that moves the player. Answering only one of those is why an overlay can end up
// hovering but never clicking - see docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <windows.h>

namespace DevTools::Input {

// What the overlay wants to see of a message, before the game does. Returns true to keep the
// message from reaching the game at all.
using MessageFn = bool (*)(HWND window, UINT message, WPARAM wparam, LPARAM lparam);

// Subclasses the game's window and takes over DirectInput's device reads. False is logged.
bool Install(HWND window, MessageFn onMessage);

// While captured, the game reads an idle keyboard and a still mouse, and messages that reach the
// overlay stop there. Its own state is untouched, so releasing hands input back mid-motion without
// a stuck key.
void SetCaptured(bool captured);

bool IsCaptured();

// The buttons and modifiers, asked for rather than waited on: the game's hold on the mouse means no
// button message is ever delivered, and the thread that draws is not the one messages arrive on, so
// its own key state is always empty.
struct RawKeys {
    bool mouse[3];
    bool control;
    bool shift;
    bool alt;
};

RawKeys PollRawKeys();

}
