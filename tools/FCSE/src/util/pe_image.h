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

// An import-table slot pointed at a detour, and the function it held before.
struct DetouredImport {
    uintptr_t* slot = nullptr;
    uintptr_t original = 0;

    // False when the image does not import `moduleName`'s `functionName` by name, or its slot is
    // empty or could not be made writable.
    bool Install(uintptr_t imageBase, const char* moduleName, const char* functionName,
                 uintptr_t detour);
    // Puts the original back. False when nothing was installed.
    bool Restore();
};

}
