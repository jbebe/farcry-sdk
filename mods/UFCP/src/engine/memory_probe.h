// Whether a pointer out of the engine can still be read, and whether it points into Dunia.
//
// Both questions are asked by more than one feature, and both have exactly one right answer that
// must not drift: an engine pointer outlives the entity behind it, and Dunia's mapped size is not
// the size the loader reports on disk.
#pragma once

#include <cstddef>

namespace UFCP {

// True while `size` bytes at `address` are committed and readable. Never cache the answer: pages
// are decommitted and reused, so a remembered result is exactly the stale one not to trust.
bool IsReadable(const void* address, size_t size);

// True while `address` points inside Dunia's mapped image. Bounds are read from the PE headers
// once, because the loader's own duniaSize is the file's size on disk and unrelated to the mapping.
bool IsInDunia(const void* address);

}
