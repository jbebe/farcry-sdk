// The Magma objects a loaded UI package exports by name, and what the notebook does with them.
#pragma once

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

// Plays an Area's timeline from `frame`, its keyframes' actions included.
void PlayFrom(void* area, int frame);

// Makes an element run its keyframe's actions again when its area's time is next set, as on the
// first time. An element runs them only on moving to another keyframe, so one with a single
// keyframe runs them once otherwise.
void Rearm(void* element);

}
