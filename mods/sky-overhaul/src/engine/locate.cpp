#include "engine/locate.h"

#include "fcse_api.h"

uintptr_t SkyOverhaul::Locate(const char* what, uintptr_t mapped, uintptr_t scanned) {
    if (mapped != scanned) {
        FCSE::Logf("%s: the address library says 0x%08zX and the pattern says 0x%08zX, taking %s",
                   what, static_cast<size_t>(mapped), static_cast<size_t>(scanned),
                   scanned != 0 ? "the pattern" : "the library");
    }
    return scanned != 0 ? scanned : mapped;
}

bool SkyOverhaul::HookAt(uintptr_t target, void* detour, void** original, const char* missing,
                         const char* what) {
    if (target == 0) {
        FCSE::Logf(missing, what);
        return false;
    }
    return FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(target), detour, original);
}
