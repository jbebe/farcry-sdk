// A vehicle's engine sound and rev counter (CVehicleTypeWheeled), with the gear changes and the RPM
// they follow handed to the plugin for the vehicles it drives.
//
// See docs/docs/engine-internals/audio-runtime.md#vehicle-sounds.
#include "engine/vehicle_sound.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "engine/sound_bank.h"
#include "fcse_api.h"

#include <atomic>
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

    constexpr ptrdiff_t kSoundType = 0x240;
    constexpr ptrdiff_t kSoundPedalParameter = 0x25C;
    constexpr ptrdiff_t kSoundCallbacks = 0x30C;

    // CVehicleTypeWheeledSoundCB::GetMultiLayer: a multilayer's game parameter, asked of the callbacks
    // at the sound +0x30C, which hold their sound at +0xC. Its entry is wildcarded for any other
    // plugin's hook there.
    using GetMultiLayerFn = float(__fastcall*)(uint8_t* callbacks, void* unused, uint32_t parameter);
    FCSE::Relocation<GetMultiLayerFn> g_getMultiLayer{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? 8B 4D 0C 85 C9 0F 84 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B C8 85 C9 8B 45 0C 89 4C 24 08 "
        "0F 84 ?? ?? ?? ?? 8B 54 24 14 3B 90 50 02 00 00 75 0F 8B 48 04 E8")};
    GetMultiLayerFn g_originalGetMultiLayer = nullptr;
    constexpr ptrdiff_t kCallbacksSound = 0x0C;

    // The sound of the vehicle whose driver is off the throttle for a gear change, or null. Asked from
    // the sound mixer.
    std::atomic<uint8_t*> g_lifted{nullptr};

    // The pedal a lifted driver's sound hears: below anything the game answers (0 to 100), so a pedal
    // curve can give a gear change a dip of its own. One without such a point holds its 0 value.
    constexpr float kLiftedPedal = -100.0f;

    float __fastcall GetMultiLayerDetour(uint8_t* callbacks, void* unused, uint32_t parameter) {
        const float value = g_originalGetMultiLayer(callbacks, unused, parameter);
        uint8_t* lifted = g_lifted.load(std::memory_order_relaxed);
        if (lifted != nullptr && At<uint8_t*>(callbacks, kCallbacksSound) == lifted &&
            parameter == At<uint32_t>(lifted, kSoundPedalParameter)) {
            return kLiftedPedal;
        }
        return value;
    }

    VehicleOverhaul::VehicleSound::EngineFn g_engine = nullptr;
    VehicleOverhaul::VehicleSound::ObserveFn g_observe = nullptr;
    std::atomic<uint32_t> g_shiftSound{0};

    // The sound the gear choice took this frame, and the RPM for it.
    uint8_t* g_driven = nullptr;
    float g_rpm = 0.0f;

    void OnGearChoice(FCSE_MidHookContext* ctx) {
        auto* sound = reinterpret_cast<uint8_t*>(ctx->esi);
        void* vehicle = VehicleOverhaul::Entity::Of(At<void*>(sound, kSoundComponent));
        if (g_observe != nullptr) {
            g_observe(vehicle, sound);
        }
        VehicleOverhaul::VehicleSound::Engine engine{};
        g_driven = nullptr;
        if (!g_engine(vehicle, engine)) {
            uint8_t* expected = sound;
            g_lifted.compare_exchange_strong(expected, nullptr);
            return;
        }
        g_lifted = engine.lifted ? sound : nullptr;
        if (engine.shiftSound != 0) {
            g_shiftSound = engine.shiftSound;
            VehicleOverhaul::SoundBank::Hold(engine.shiftSound);
        }
        g_driven = sound;
        g_rpm = engine.rpm;
        At<int32_t>(sound, kSoundGear) = kGearSlot;
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
    if (!api->MidHook(g_gearChoice.get(), &OnGearChoice) || !api->MidHook(g_rpmDone.get(), &OnRpmDone)) {
        return false;
    }
    // Without it the engine keeps its throttle sound through a gear change; it runs on.
    if (!g_getMultiLayer) {
        api->Log("vehicle sound: GetMultiLayer was not found in this build; gear changes keep the throttle on");
    } else {
        api->Hook(reinterpret_cast<void*>(g_getMultiLayer.address()), reinterpret_cast<void*>(&GetMultiLayerDetour),
                  reinterpret_cast<void**>(&g_originalGetMultiLayer));
    }
    return true;
}

void Observe(ObserveFn observe) { g_observe = observe; }

uint32_t ShiftSound() { return g_shiftSound; }

int32_t Type(const uint8_t* sound) { return At<int32_t>(sound, kSoundType); }

void* Callbacks(uint8_t* sound) { return sound + kSoundCallbacks; }

}
