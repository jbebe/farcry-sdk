#pragma once

namespace LooseMods {

// Installs the inline hook on Dunia.dll's VFS_ResolvePath (RVA 0x002358a0 in the confirmed
// Steam v1.03 build, this = g_VFSResolver at Dunia.dll+0x0ff0ef8). See
// reverse/dunia/archive_loading.md for the full derivation.
//
// Refuses to hook (logs and returns false) if Dunia.dll isn't loaded yet, or if its module size
// doesn't match the known-good v1.03 build - see kExpectedDuniaImageSize in hook_vfs.cpp.
bool InstallVfsHook();

// Disables and removes the hook. Safe to call even if InstallVfsHook never succeeded.
void RemoveVfsHook();

} // namespace LooseMods
