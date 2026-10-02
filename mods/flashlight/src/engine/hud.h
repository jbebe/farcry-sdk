// Magma objects a loaded UI package exports by name, and the calls the HUD drives its groups with.
#pragma once

#include <cstdint>

namespace Flashlight::Hud {

// Logs when this build lacks the Magma calls; Find then finds nothing.
void Install();

// The live object exported under the name whose magma::Id is `id`, or null while no loaded package
// exports it.
void* Find(uint32_t id);

void SetVisible(void* element, bool visible);

// Jumps `area`'s timeline to `frame` and plays it from there, as the HUD shows and hides its groups.
void PlayFrom(void* area, int frame);

}
