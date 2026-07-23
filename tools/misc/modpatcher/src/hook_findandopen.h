#pragma once

namespace LooseMods {

// Installs the hash-level loose-file hook on Dunia.dll's ArchiveEntry_FindAndOpen - the shared
// choke point every hash-based archive open funnels through, including the world/level streamer
// that bypasses VFS_ResolvePath. Requires BuildLooseIndex() to have run first. Complementary to
// InstallVfsHook (the string-path hook); the two never both act on a single request.
bool InstallFindAndOpenHook();
void RemoveFindAndOpenHook();

} // namespace LooseMods
