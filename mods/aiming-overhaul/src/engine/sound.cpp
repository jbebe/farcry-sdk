// Playing a sound event by id through the game's sound system. See
// docs/docs/file-formats/spk.md#what-loads-the-child-bank-depload.
#include "engine/sound.h"

#include "fcse_api.h"

#include <algorithm>
#include <array>

namespace {
    // A counted reference to a bank; `requested` says whether it holds a load request.
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

    // The banks held, by the id each is named after.
    struct Held {
        uint32_t id;
        SoundResourceRef ref;
    };
    constexpr size_t kMostHeld = 16;

    bool g_ready = false;
    std::array<Held, kMostHeld> g_held = {};
    size_t g_heldCount = 0;

    template <typename Fn>
    Fn Slot(void* object, size_t index) {
        return reinterpret_cast<Fn>((*static_cast<void***>(object))[index]);
    }
}

void AimingOverhaul::Sound::Install() {
    g_ready = g_getSoundSystem && g_getFromSoundId;
    if (!g_ready) {
        FCSE::ApiPointer()->Log("sound: the sound system calls were not found in this build");
    }
}

void AimingOverhaul::Sound::Hold(uint32_t id) {
    const auto held = g_held.begin() + g_heldCount;
    if (!g_ready || std::any_of(g_held.begin(), held, [id](const Held& h) { return h.id == id; })) {
        return;
    }
    if (g_heldCount == kMostHeld) {
        static bool s_logged = false;
        if (!s_logged) {
            s_logged = true;
            FCSE::Logf("sound: no room to hold bank 0x%08X loaded, so it never plays", id);
        }
        return;
    }
    Held& hold = g_held[g_heldCount++];
    hold.id = id;
    g_getFromSoundId(&hold.ref, id, nullptr);
    if (hold.ref.resource != nullptr) {
        Slot<RequestLoadFn>(hold.ref.resource, kRequestLoadSlot)(hold.ref.resource);
    }
}

void AimingOverhaul::Sound::Play(uint32_t id, int32_t type) {
    void* system = g_ready ? g_getSoundSystem() : nullptr;
    if (system != nullptr) {
        Slot<PlaySoundFn>(system, kPlaySoundSlot)(system, id, type, nullptr, 0.0f);
    }
}
