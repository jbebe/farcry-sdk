#include "engine/sound.h"

#include "fcse_api.h"

namespace {
    // The engine's counted reference to a bank, which it fills; `requested` says whether it holds a
    // load request.
    struct SoundResourceRef {
        void* resource;
        bool requested;
    };

    using GetSoundSystemFn = void*(__cdecl*)();
    using GetFromSoundIdFn = SoundResourceRef*(__cdecl*)(SoundResourceRef* out, uint32_t id,
                                                         const char* language);
    using RequestLoadFn = void(__thiscall*)(void* resource);
    using PlaySoundFn = uint32_t(__thiscall*)(void* system, uint32_t id, int32_t type,
                                              void* callbacks, float volume);

    FCSE::Relocation<GetSoundSystemFn> g_getSoundSystem{FCSE::Uplay(0x006215B0)};
    FCSE::Relocation<GetFromSoundIdFn> g_getFromSoundId{FCSE::Uplay(0x006242F0)};

    constexpr size_t kRequestLoadSlot = 0x08 / sizeof(void*);
    constexpr size_t kPlaySoundSlot = 0x9C / sizeof(void*);
    constexpr int kMostHeld = 8;
    // The sound type of everything not placed in the world.
    constexpr int32_t kEffectNonLocal = 12;

    struct Held {
        uint32_t id;
        SoundResourceRef ref;
    };

    bool g_ready = false;
    Held g_held[kMostHeld] = {};
    int g_heldCount = 0;

    template <typename Fn>
    Fn Slot(void* object, size_t index) {
        return reinterpret_cast<Fn>((*static_cast<void***>(object))[index]);
    }
}

bool SkyOverhaul::Sound::Install() {
    g_ready = g_getSoundSystem && g_getFromSoundId;
    if (!g_ready) {
        FCSE::Logf("sound: the sound system calls were not found in this build - no thunder");
    }
    return g_ready;
}

void SkyOverhaul::Sound::Hold(uint32_t id) {
    if (!g_ready || g_heldCount == kMostHeld) {
        return;
    }
    for (int i = 0; i < g_heldCount; i++) {
        if (g_held[i].id == id) {
            return;
        }
    }
    Held& held = g_held[g_heldCount++];
    held.id = id;
    g_getFromSoundId(&held.ref, id, nullptr);
    if (held.ref.resource != nullptr) {
        Slot<RequestLoadFn>(held.ref.resource, kRequestLoadSlot)(held.ref.resource);
    }
}

void SkyOverhaul::Sound::Play(uint32_t id) {
    void* system = g_ready ? g_getSoundSystem() : nullptr;
    if (system != nullptr) {
        Slot<PlaySoundFn>(system, kPlaySoundSlot)(system, id, kEffectNonLocal, nullptr, 0.0f);
    }
}
