// Sound banks, which the game loads only for the sounds its data names.
//
// See docs/docs/file-formats/spk.md.
#include "engine/sound_bank.h"

#include "fcse_api.h"

#include <algorithm>
#include <array>

namespace {
    struct ResourceRef {
        void* resource = nullptr;
        bool requested = false;
    };
    using GetFromSoundIdFn = ResourceRef*(__cdecl*)(ResourceRef* out, uint32_t id, const char* language);
    using RequestLoadFn = void(__thiscall*)(void* resource);
    constexpr size_t kRequestLoadSlot = 0x8 / sizeof(void*);

    // CSoundResource::GetFromSoundId: a counted reference to the bank named after a sound id, created
    // unloaded when nothing holds it yet.
    FCSE::Relocation<GetFromSoundIdFn> g_getFromSoundId{FCSE::Uplay(0x006242F0)};

    std::array<uint32_t, 8> g_held{};
}

namespace VehicleOverhaul::SoundBank {

bool Install() {
    if (!g_getFromSoundId) {
        FCSE::ApiPointer()->Log("sound bank: GetFromSoundId was not found in this build");
        return false;
    }
    return true;
}

void Hold(uint32_t id) {
    if (!g_getFromSoundId || std::find(g_held.begin(), g_held.end(), id) != g_held.end()) {
        return;
    }
    const auto free = std::find(g_held.begin(), g_held.end(), 0u);
    if (free == g_held.end()) {
        return;
    }
    *free = id;

    // The reference is never released, so the bank stays.
    ResourceRef ref;
    g_getFromSoundId(&ref, id, nullptr);
    if (ref.resource != nullptr) {
        (*reinterpret_cast<RequestLoadFn**>(ref.resource))[kRequestLoadSlot](ref.resource);
    }
}

}
