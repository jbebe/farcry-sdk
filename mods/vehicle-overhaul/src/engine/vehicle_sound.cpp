// A vehicle's engine sound and rev counter (CVehicleTypeWheeled), with the gear changes and the RPM
// they follow handed to the plugin for the vehicles it drives.
//
// See docs/docs/engine-internals/audio-runtime.md#vehicle-sounds.
#include "engine/vehicle_sound.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "engine/sound_bank.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    using VehicleOverhaul::At;

    // A component of the vehicle's own entity.
    constexpr ptrdiff_t kSoundComponent = 0x04;
    constexpr ptrdiff_t kSoundGear = 0x36C;
    constexpr ptrdiff_t kSoundRpm = 0x370;
    constexpr ptrdiff_t kSoundShiftDirection = 0x374;

    // The emulated gear a driven vehicle is kept in: the rev counter divides by its top RPM.
    constexpr int32_t kGearSlot = 2;

    // The update's choice of emulated gear, with ESI the sound and the road speed at [esp+0x34]. The
    // pattern runs on to where the gear is stored and its shift sound played, and to the RPM step
    // after it, the two places a driven vehicle is sent instead.
    FCSE::Relocation<uint8_t*> g_gearChoice{FCSE::Pattern(
        "8B 86 6C 03 00 00 85 C0 F3 0F 10 44 24 34 76 1D F3 0F 10 8C 86 3C 03 00 00 0F 2F C8 76 0F C7 86 "
        "74 03 00 00 FF FF FF FF 83 C0 FF EB 1C 83 F8 02 73 49 0F 2F 84 86 48 03 00 00 76 3F C7 86 74 03 "
        "00 00 01 00 00 00 83 C0 01 89 86 6C 03 00 00 E8 ?? ?? ?? ?? D9 EE 8B 10 8B 92 9C 00 00 00 51 D9 "
        "1C 24 8D 8E 0C 03 00 00 51 8B 8E 40 02 00 00 51 55 8B C8 FF D2 89 86 7C 02 00 00 8B 86 7C 02 00 "
        "00")};
    constexpr size_t kStoreGear = 0x49;
    constexpr size_t kNoShift = 0x7B;

    // Where the update has its RPM, with ESI the sound.
    FCSE::Relocation<uint8_t*> g_rpmDone{FCSE::Pattern(
        "A1 ?? ?? ?? ?? 8B CE 89 44 24 28 E8 ?? ?? ?? ?? 8B D8 85 DB 89 5C 24 10 0F 84 ?? ?? ?? ?? 8B 4E")};

    VehicleOverhaul::VehicleSound::EngineFn g_engine = nullptr;

    // The sound the gear choice took this frame, and the RPM for it.
    uint8_t* g_driven = nullptr;
    float g_rpm = 0.0f;

    void OnGearChoice(FCSE_MidHookContext* ctx) {
        auto* sound = reinterpret_cast<uint8_t*>(ctx->esi);
        VehicleOverhaul::VehicleSound::Engine engine{};
        g_driven = nullptr;
        if (!g_engine(VehicleOverhaul::Entity::Of(At<void*>(sound, kSoundComponent)), engine)) {
            return;
        }
        g_driven = sound;
        g_rpm = engine.rpm;
        At<int32_t>(sound, kSoundGear) = kGearSlot;
        if (engine.shiftSound != 0) {
            VehicleOverhaul::SoundBank::Hold(engine.shiftSound);
        }
        if (engine.shift != 0) {
            At<int32_t>(sound, kSoundShiftDirection) = engine.shift;
            // EBP holds the event the store goes on to play, picked by the vehicle's reliability.
            if (engine.shiftSound != 0) {
                ctx->ebp = engine.shiftSound;
            }
            ctx->eax = kGearSlot;
            ctx->eip = g_gearChoice.address() + kStoreGear;
        } else {
            ctx->eip = g_gearChoice.address() + kNoShift;
        }
    }

    void OnRpmDone(FCSE_MidHookContext* ctx) {
        if (reinterpret_cast<uint8_t*>(ctx->esi) == g_driven) {
            At<float>(g_driven, kSoundRpm) = g_rpm;
            g_driven = nullptr;
        }
    }
}

namespace VehicleOverhaul::VehicleSound {

bool Install(EngineFn engine) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_gearChoice || !g_rpmDone) {
        api->Log("vehicle sound: the update's gear and RPM steps were not found in this build");
        return false;
    }
    // Without it a gear change sound's bank never loads and the change is silent; the engine runs on.
    SoundBank::Install();
    g_engine = engine;
    return api->MidHook(g_gearChoice.get(), &OnGearChoice) && api->MidHook(g_rpmDone.get(), &OnRpmDone);
}

}
