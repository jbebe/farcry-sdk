#include "api/patch.h"

#include "caller_identity.h"
#include "log.h"

#include <windows.h>

#include <cstdint>
#include <cstring>
#include <intrin.h>
#include <string>
#include <vector>

namespace FCSE {

namespace {
    struct ClaimedRange {
        uintptr_t start;
        uintptr_t end; // exclusive
        std::string owner;
    };

    std::vector<ClaimedRange> g_claims;

    bool RangesOverlap(uintptr_t aStart, uintptr_t aEnd, uintptr_t bStart, uintptr_t bEnd) {
        return aStart < bEnd && bStart < aEnd;
    }
}

bool PatchManager::Patch(void* address, const void* data, size_t size) {
    const std::string caller = ResolveCallerModuleName(_ReturnAddress());

    if (address == nullptr || data == nullptr || size == 0) {
        Log::Write(caller, "Patch() called with a null address/data or zero size, rejected");
        return false;
    }

    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    uintptr_t end = start + size;

    for (const ClaimedRange& claim : g_claims) {
        if (claim.owner != caller && RangesOverlap(start, end, claim.start, claim.end)) {
            Log::Write(caller, "Patch conflict: byte range overlaps a range already claimed by '" +
                                   claim.owner + "', rejected");
            return false;
        }
    }

    DWORD oldProtect = 0;
    if (!VirtualProtect(address, size, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        Log::Write(caller,
                   "Patch() VirtualProtect failed, error " + std::to_string(GetLastError()));
        return false;
    }

    std::memcpy(address, data, size);

    DWORD ignored = 0;
    VirtualProtect(address, size, oldProtect, &ignored);
    FlushInstructionCache(GetCurrentProcess(), address, size);

    g_claims.push_back({start, end, caller});
    Log::Write(caller, "Patch applied (" + std::to_string(size) + " bytes)");
    return true;
}

} // namespace FCSE
