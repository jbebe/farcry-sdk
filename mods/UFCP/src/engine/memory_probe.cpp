// Whether a pointer out of the engine can still be read, and whether it points into Dunia.
#include "engine/memory_probe.h"

#include "fcse_api.h"

#include <windows.h>

#include <cstdint>

namespace {
    uintptr_t g_duniaBase = 0;
    uintptr_t g_duniaEnd = 0;

    // Walked once and kept. The headers are in the image, so they cannot move under this.
    void ResolveDuniaBounds() {
        if (g_duniaEnd != 0) {
            return;
        }

        const uintptr_t base = FCSE::ApiPointer()->duniaBase;
        if (base == 0) {
            return;
        }

        const IMAGE_DOS_HEADER* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
            return;
        }

        const IMAGE_NT_HEADERS* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) {
            return;
        }

        g_duniaBase = base;
        g_duniaEnd = base + nt->OptionalHeader.SizeOfImage;
    }
}

namespace UFCP {

bool IsReadable(const void* address, size_t size) {
    MEMORY_BASIC_INFORMATION region;
    if (address == nullptr || VirtualQuery(address, &region, sizeof(region)) != sizeof(region)) {
        return false;
    }

    if (region.State != MEM_COMMIT || (region.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0) {
        return false;
    }

    const uint8_t* start = static_cast<const uint8_t*>(address);
    const uint8_t* end = static_cast<const uint8_t*>(region.BaseAddress) + region.RegionSize;
    return start + size <= end;
}

bool IsInDunia(const void* address) {
    ResolveDuniaBounds();

    const uintptr_t at = reinterpret_cast<uintptr_t>(address);
    return g_duniaBase != 0 && at >= g_duniaBase && at < g_duniaEnd;
}

}
