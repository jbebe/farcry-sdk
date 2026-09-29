// Reverb: gives CSoundSystem::PlaySoundReverb a body. Retail ships it empty, so no building, region
// or mix preset ever changes DARE's reverb.
#include "sound_system.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    using namespace SoundOverhaul::SoundSystem;

    // The empty body's first byte: `ret 4`.
    constexpr uint8_t kRetN = 0xC2;

    // Effect_Non_Loc: unpositioned, and silenced by none of the retail mix presets.
    constexpr int kSoundType = 12;

    using PlaySoundFn = uint32_t(__thiscall*)(void* self, uint32_t event, int type,
                                              void* callbacks, float volume);

    // A reverb event is a type-8 DARE event, so playing it sets the reverb.
    void __fastcall PlaySoundReverb(void* self, void*, uint32_t event) {
        const auto playSound =
            reinterpret_cast<PlaySoundFn>((*static_cast<void***>(self))[kPlaySound]);
        playSound(self, event, kSoundType, nullptr, 0.0f);
    }
}

void ApplyReverbFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void** vtable = Vtable();
    if (vtable == nullptr) {
        api->Log("reverb fix: CSoundSystem's constructor was not found in this build - not fixed");
        return;
    }

    if (*static_cast<const uint8_t*>(vtable[kPlaySoundReverb]) != kRetN) {
        api->Log("reverb fix: PlaySoundReverb is not the empty body - left alone");
        return;
    }

    void* body = reinterpret_cast<void*>(&PlaySoundReverb);
    if (api->Patch(&vtable[kPlaySoundReverb], &body, sizeof(body))) {
        api->Log("reverb fix applied: buildings, regions and mix presets now set the reverb");
    }
}
