#include "sound_system.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    // CSoundSystem's constructor storing its vtable: `mov dword ptr [esi], vtable`.
    FCSE::Relocation<uint8_t*> g_constructor{
        FCSE::Pattern("53 56 8B F1 8D 4C 24 0A C7 06 ?? ?? ?? ?? 8D 46 08 51 33 DB")};
    constexpr ptrdiff_t kVtableOperand = 10;
}

void** SoundOverhaul::SoundSystem::Vtable() {
    return g_constructor ? *reinterpret_cast<void***>(g_constructor.address() + kVtableOperand) : nullptr;
}
