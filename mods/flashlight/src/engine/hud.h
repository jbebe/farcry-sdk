// Magma objects a loaded UI package exports by name, and the calls the HUD drives its groups with.
#pragma once

namespace Flashlight::Hud {

// False, and logged, when this build lacks the Magma calls.
bool Install();

// The live object `name` is exported as, or null while no loaded package exports it.
void* Find(const char* name);

void SetVisible(void* element, bool visible);

// Jumps `area`'s timeline to `frame` and plays it from there, as the HUD shows and hides its groups.
void PlayFrom(void* area, int frame);

}
