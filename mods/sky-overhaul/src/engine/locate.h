// Where an engine function is, from the two ways a plugin can find one.
#pragma once

#include <cstdint>

namespace SkyOverhaul {

// The address library's answer and the pattern's, as one address: the pattern wins a disagreement,
// which is logged under `what`, because it is checked against both shipped builds before every
// release. Zero when neither found it.
uintptr_t Locate(const char* what, uintptr_t mapped, uintptr_t scanned);

// Hooks the function at `target`, the original kept in `original`. With no target it logs `missing`,
// a format that takes `what`, and gives false.
bool HookAt(uintptr_t target, void* detour, void** original, const char* missing, const char* what);

template <typename Fn>
bool HookAt(uintptr_t target, Fn detour, Fn& original, const char* missing, const char* what) {
    return HookAt(target, reinterpret_cast<void*>(detour), reinterpret_cast<void**>(&original),
                  missing, what);
}

// Where the rel32 `call` instruction at `call` goes.
template <typename T>
T CallTarget(const uint8_t* call) {
    return reinterpret_cast<T>(call + 5 + *reinterpret_cast<const int32_t*>(call + 1));
}

}
