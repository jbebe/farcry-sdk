// Playing a sound event by id through the game's sound system. See
// docs/docs/file-formats/spk.md#what-loads-the-child-bank-depload.
#include "engine/sound.h"

#include "fcse_api.h"

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

    FCSE::Relocation<GetSoundSystemFn> g_getSoundSystemSite{FCSE::Uplay(0x006215B0)};
    FCSE::Relocation<GetFromSoundIdFn> g_getFromSoundIdSite{FCSE::Uplay(0x006242F0)};

    constexpr size_t kRequestLoadSlot = 0x08 / sizeof(void*);
    constexpr size_t kPlaySoundSlot = 0x9C / sizeof(void*);

    GetSoundSystemFn g_getSoundSystem = nullptr;
    GetFromSoundIdFn g_getFromSoundId = nullptr;
    SoundResourceRef g_held{};
    bool g_holding = false;

    template <typename Fn>
    Fn Slot(void* object, size_t index) {
        return reinterpret_cast<Fn>((*static_cast<void***>(object))[index]);
    }
}

namespace Flashlight::Sound {

bool Install() {
    if (!g_getSoundSystemSite || !g_getFromSoundIdSite) {
        FCSE::ApiPointer()->Log("sound: the sound system calls were not found in this build");
        return false;
    }
    g_getSoundSystem = g_getSoundSystemSite.get();
    g_getFromSoundId = g_getFromSoundIdSite.get();
    return true;
}

void Hold(uint32_t id) {
    if (g_holding || g_getFromSoundId == nullptr) {
        return;
    }
    g_holding = true;
    g_getFromSoundId(&g_held, id, nullptr);
    if (g_held.resource != nullptr) {
        Slot<RequestLoadFn>(g_held.resource, kRequestLoadSlot)(g_held.resource);
    }
}

void Play(uint32_t id, int32_t type) {
    void* system = g_getSoundSystem != nullptr ? g_getSoundSystem() : nullptr;
    if (system != nullptr) {
        Slot<PlaySoundFn>(system, kPlaySoundSlot)(system, id, type, nullptr, 0.0f);
    }
}

}
