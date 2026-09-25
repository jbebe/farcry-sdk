// Reverb: gives CSoundSystem::PlaySoundReverb a body. Retail ships it empty, so no building, region
// or mix preset ever changes DARE's reverb.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // CSoundSystem's constructor storing its vtable: `mov dword ptr [esi], vtable`.
    FCSE::Relocation<uint8_t*> g_constructor{
        FCSE::Pattern("53 56 8B F1 8D 4C 24 0A C7 06 ?? ?? ?? ?? 8D 46 08 51 33 DB")};
    constexpr ptrdiff_t kVtableOperand = 10;

    constexpr size_t kPlaySoundReverb = 0x98 / sizeof(void*);
    constexpr size_t kPlaySound = 0x9C / sizeof(void*);

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

    if (!g_constructor) {
        api->Log("reverb fix: CSoundSystem's constructor was not found in this build - not fixed");
        return;
    }

    void** vtable = *reinterpret_cast<void***>(g_constructor.address() + kVtableOperand);
    if (*static_cast<const uint8_t*>(vtable[kPlaySoundReverb]) != kRetN) {
        api->Log("reverb fix: PlaySoundReverb is not the empty body - left alone");
        return;
    }

    void* body = reinterpret_cast<void*>(&PlaySoundReverb);
    if (api->Patch(&vtable[kPlaySoundReverb], &body, sizeof(body))) {
        api->Log("reverb fix applied: buildings, regions and mix presets now set the reverb");
    }
}
