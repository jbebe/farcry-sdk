#pragma once

#include <cstddef>

// Tier 3 of the plugin API: direct byte patching for small constant/branch-flip edits. Backs
// FCSE_PluginAPI::Patch - applies them live, in-process, at plugin-load time, so any number of
// plugins can each patch their own byte ranges without one shared pre-patched Dunia.dll file.
namespace FCSE {

class PatchManager {
public:
    // Backs FCSE_PluginAPI::Patch. Handles the VirtualProtect dance so `address` doesn't need to
    // already be writable, then FlushInstructionCache so a subsequently-executed patched
    // instruction is guaranteed to see the new bytes. Captures the calling plugin's identity
    // itself via _ReturnAddress(). Returns false (logged) if the byte range overlaps a range a
    // *different* plugin already claimed this run - overlap with your own earlier claim is fine.
    static bool Patch(void* address, const void* data, size_t size);
};

} // namespace FCSE
