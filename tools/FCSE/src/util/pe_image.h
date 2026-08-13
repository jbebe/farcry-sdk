#pragma once

#include <cstddef>
#include <cstdint>
#include <windows.h>

namespace FCSE {

// Reads of a module's own PE headers as the loader mapped them, rather than a re-read of the file.
// FCSE is x86-only, so IMAGE_NT_HEADERS32 is the only form these can be.

// The mapped image's NT headers, or nullptr if `imageBase` is not a PE image.
const IMAGE_NT_HEADERS32* PeHeaders(const void* imageBase);

// The first section marked executable. Located by characteristics rather than by the name ".text":
// the name is a convention, the executable flag is what decides whether code can live there.
bool FindExecutableSection(const void* imageBase, const uint8_t** begin, size_t* size);

// The import-table slot holding `moduleName`'s `functionName`, for callers that detour an import
// by overwriting it. Returns nullptr when the image does not import that function by name.
uintptr_t* FindImportSlot(uintptr_t imageBase, const char* moduleName, const char* functionName);

}
