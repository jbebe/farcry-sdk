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
