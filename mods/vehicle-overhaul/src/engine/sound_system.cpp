// The game's sound system (CSoundSystem): every sound it starts, and starting and stopping one.
//
// See docs/docs/engine-internals/audio-runtime.md.
#include "engine/sound_system.h"

#include "fcse_api.h"

namespace {
    using VehicleOverhaul::SoundSystem::kNone;

    using GetSoundSystemFn = void* (*)();
    using PlaySoundFn = uint32_t(__fastcall*)(void* system, void* unused, uint32_t id, int32_t type,
                                              void* callbacks, float volume);
    using StopSoundFn = void(__thiscall*)(void* system, uint32_t handle, float fade, uint32_t flags);
    constexpr size_t kStopSoundSlot = 0xA8 / sizeof(void*);

    FCSE::Relocation<GetSoundSystemFn> g_getSoundSystem{FCSE::Uplay(0x006215B0)};
    // CSoundSystem::PlaySound, vtable +0x9C, which the address library lacks for the GOG build. Its entry
    // is wildcarded for any other plugin's hook there.
    FCSE::Relocation<PlaySoundFn> g_playSound{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? 83 FF FF 8B F1 75 08 5F 83 C8 FF 5E C2 10 00 8B 06 8B 50 1C FF D2 84 C0 74 ED "
        "8B 44 24 10 83 F8 FF 74 E4 85 C0 7C E0 3B 46 44 7D DB 8B 4E 70 80 3C 08 00 77 D2")};
    PlaySoundFn g_originalPlaySound = nullptr;

    VehicleOverhaul::SoundSystem::StartFn g_start = nullptr;

    uint32_t __fastcall PlaySoundDetour(void* system, void* unused, uint32_t id, int32_t type, void* callbacks,
                                        float volume) {
        if (id != kNone && !g_start(id, type, callbacks)) {
            return kNone;
        }
        return g_originalPlaySound(system, unused, id, type, callbacks, volume);
    }

    // CSoundSystem::SetTypeVolume, vtable +0x68: a sound type's volume in dB, which the mix sends; the sound
    // engine clamps it to -96..0. The last word is passed on beside it.
    using SetTypeVolumeFn = void(__fastcall*)(void* system, void* unused, int32_t type, float volume, uint32_t fade);
    FCSE::Relocation<SetTypeVolumeFn> g_setTypeVolume{FCSE::Pattern(
        "?? ?? ?? ?? ?? 8B 50 1C FF D2 84 C0 0F 84 D5 00 00 00 57 8B 7C 24 0C 83 FF FF 0F 84 C6 00 00 00 85 FF "
        "0F 8C BE 00 00 00 3B 7E 44 0F 8D B5 00 00 00 8B 46 40 F3 0F 10 44 24 10 F3 0F 11 04 B8 8B 4E 4C 83 3C "
        "B9 00")};
    SetTypeVolumeFn g_originalSetTypeVolume = nullptr;

    VehicleOverhaul::SoundSystem::TypeVolumeFn g_typeVolume = nullptr;

    // What the mix last sent each type, to send again through the adjustment.
    struct Sent {
        void* system = nullptr;
        float volume = 0.0f;
        uint32_t fade = 0;
    };
    Sent g_sent[VehicleOverhaul::SoundSystem::kTypeCount];

    void __fastcall SetTypeVolumeDetour(void* system, void* unused, int32_t type, float volume, uint32_t fade) {
        if (type >= 0 && type < VehicleOverhaul::SoundSystem::kTypeCount) {
            g_sent[type] = {system, volume, fade};
            volume = g_typeVolume(type, volume);
        }
        g_originalSetTypeVolume(system, unused, type, volume, fade);
    }
}

namespace VehicleOverhaul::SoundSystem {

bool Install(StartFn start) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_getSoundSystem || !g_playSound) {
        api->Log("sound system: GetSoundSystem or PlaySound was not found in this build");
        return false;
    }
    g_start = start;
    return api->Hook(reinterpret_cast<void*>(g_playSound.address()), reinterpret_cast<void*>(&PlaySoundDetour),
                     reinterpret_cast<void**>(&g_originalPlaySound));
}

uint32_t Play(uint32_t id, int32_t type, void* callbacks) {
    void* system = g_getSoundSystem();
    return system != nullptr ? g_playSound(system, nullptr, id, type, callbacks, 0.0f) : kNone;
}

void Stop(uint32_t handle) {
    void* system = g_getSoundSystem();
    if (system != nullptr && handle != kNone) {
        (*reinterpret_cast<StopSoundFn**>(system))[kStopSoundSlot](system, handle, 0.0f, 0);
    }
}

bool AdjustTypeVolumes(TypeVolumeFn adjust) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_setTypeVolume) {
        api->Log("sound system: SetTypeVolume was not found in this build");
        return false;
    }
    g_typeVolume = adjust;
    return api->Hook(reinterpret_cast<void*>(g_setTypeVolume.address()), reinterpret_cast<void*>(&SetTypeVolumeDetour),
                     reinterpret_cast<void**>(&g_originalSetTypeVolume));
}

void ResendTypeVolumes() {
    for (int32_t type = 0; type < kTypeCount; ++type) {
        const Sent& sent = g_sent[type];
        if (sent.system != nullptr && g_originalSetTypeVolume != nullptr) {
            SetTypeVolumeDetour(sent.system, nullptr, type, sent.volume, sent.fade);
        }
    }
}

}
