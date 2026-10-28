// The Magma objects a loaded UI package exports by name, and what the notebook does with them.
#pragma once

#include <cstdint>

namespace InventoryOverhaul::Magma {

// False when the calls were not found in this build.
bool Install();

// The element exported as `name`, or null while no loaded package exports it.
void* Find(const char* name);

void SetVisible(void* element, bool visible);

// A Text element's string.
void SetText(void* element, const wchar_t* text);

// An Image element's picture made another's, as the pause menu's Buddies page swaps a portrait.
void SetPicture(void* image, const void* from);

// Shows an Area's timeline at `milliseconds`, without running its keyframes' actions.
void SetTime(void* area, uint32_t milliseconds);

// The milliseconds an Area shows each frame of its timeline for.
uint16_t TimePerFrame(const void* area);

}
