// CSoundSystem through its vtable, as mods/flashlight and mods/vehicle-overhaul reach it. See
// docs/docs/file-formats/spk.md#what-loads-the-child-bank-depload.
#include "engine/sound.h"

#include "engine/memory.h"
#include "fcse_api.h"

namespace InventoryOverhaul::Sound {

namespace {
    // A counted reference to a bank; `requested` says whether it holds a load request.
    struct SoundResourceRef {
        void* resource;
        bool requested;
    };

    using GetSoundSystemFn = void*(__cdecl*)();
    using GetFromSoundIdFn = SoundResourceRef*(__cdecl*)(SoundResourceRef* out, uint32_t id, const char* language);
    using RequestLoadFn = void(__thiscall*)(void* resource);
    using PlaySoundFn = uint32_t(__thiscall*)(void* system, uint32_t id, int32_t type, void* callbacks, float volume);
    using StopSoundFn = void(__thiscall*)(void* system, uint32_t handle, float fade, uint32_t flags);
    using GetSoundLengthFn = float(__thiscall*)(void* system, uint32_t id);

    FCSE::Relocation<GetSoundSystemFn> g_getSoundSystem{FCSE::Uplay(0x006215B0)};
    FCSE::Relocation<GetFromSoundIdFn> g_getFromSoundId{FCSE::Uplay(0x006242F0)};

    constexpr size_t kRequestLoadSlot = 0x08 / sizeof(void*);
    constexpr size_t kPlaySoundSlot = 0x9C / sizeof(void*);
    constexpr size_t kStopSoundSlot = 0xA8 / sizeof(void*);
    constexpr size_t kGetSoundLengthSlot = 0xC0 / sizeof(void*);

    bool g_ready = false;
    SoundResourceRef g_held{};
    bool g_holding = false;

    void* System() { return g_ready ? g_getSoundSystem() : nullptr; }
}

bool Install() {
    g_ready = g_getSoundSystem && g_getFromSoundId;
    return g_ready;
}

void Hold(uint32_t id) {
    if (!g_ready || g_holding) {
        return;
    }
    g_holding = true;
    g_getFromSoundId(&g_held, id, nullptr);
    if (g_held.resource != nullptr) {
        Method<RequestLoadFn>(g_held.resource, kRequestLoadSlot)(g_held.resource);
    }
}

uint32_t Play(uint32_t id, int32_t type) {
    void* system = System();
    return system != nullptr ? Method<PlaySoundFn>(system, kPlaySoundSlot)(system, id, type, nullptr, 0.0f) : kNone;
}

void Stop(uint32_t handle, float fade) {
    void* system = System();
    if (system != nullptr && handle != kNone) {
        Method<StopSoundFn>(system, kStopSoundSlot)(system, handle, fade, 0);
    }
}

float Length(uint32_t id) {
    void* system = System();
    return system != nullptr ? Method<GetSoundLengthFn>(system, kGetSoundLengthSlot)(system, id) : -1.0f;
}

}
