#pragma once

#include <cstdint>

namespace LooseMods {

// Resolves Dunia.dll's actual load base, but only after verifying it is the exact Steam v1.03
// build our hardcoded RVAs were derived from. Returns false (and logs the reason) if Dunia.dll
// isn't loaded yet, its path/size can't be read, or the on-disk size doesn't match - in which
// case no RVA-based hook may be installed, because every hardcoded address would be a guess.
//
// Shared by every hook in this DLL so the build-safety gate lives in exactly one place.
bool GetVerifiedDuniaBase(uintptr_t& outBase);

} // namespace LooseMods
