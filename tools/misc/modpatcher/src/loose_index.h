#pragma once

#include <cstdint>
#include <string>

// The hash side of the loose-file loader (Project 1, Phase 2). Some engine paths - notably the
// world/level streamer (LevelAsset_OpenStream) that loads terrain, meshes and textures - never go
// through VFS_ResolvePath with a string path; they hash the path themselves and hit the archive
// lookup directly (see reverse/dunia/archive_loading.md "Known coverage gap"). To override those,
// we can't intercept a string, so instead we pre-hash every file under Data_Win32\Loose\ with the
// engine's exact algorithm and match on the resulting 32-bit hash at the lookup choke point.
namespace LooseMods {

// Re-implementation of Dunia.dll's path normalizer (FUN_102356d0): '/'->'\\', ASCII-lowercase,
// collapse runs of separators, strip one leading separator. Verified byte-for-byte against real
// .fat hash tables (see the offline cross-check in the plan).
std::string NormalizePath(const char* path);

// CRC32 (standard reflected 0xEDB88320, init/final ~0) of NormalizePath(path) - Dunia.dll's
// FUN_10229440 applied to the normalized string. This is the exact key the engine binary-searches
// each mounted .fat index for.
uint32_t Crc32Path(const char* path);

// Walks Data_Win32\Loose\ recursively and indexes hash -> absolute-loose-path for every file
// found. Call once, single-threaded (from DllMain, before any hook can fire); the index is
// read-only afterwards so concurrent lookups from engine threads are safe. A missing Loose folder
// is not an error - the index just stays empty and every lookup misses.
void BuildLooseIndex();

// Returns the absolute loose-file path indexed for `hash`, or nullptr if none. Read-only and
// thread-safe once BuildLooseIndex has completed.
const std::string* LookupLooseByHash(uint32_t hash);

} // namespace LooseMods
