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

}
