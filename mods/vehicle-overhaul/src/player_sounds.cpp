// The car the player drives plays the overhaul's own engine and wheel sounds; every other car the game's.
//
// The overhaul's sounds sit under ids of its own (data/own_sounds.py), which the game's data never names,
// so its banks are held from the first sound update on. While the player is in a car its slots hold them
// in place of the game's, written before the update that starts the car's engine or its wheels' rolling.
// The engine's change only while the engine is off, so a car the player leaves with it running plays the
// overhaul's engine-off, and one whose engine already runs as they get in keeps the game's until it stops.
#include "player_sounds.h"

#include "engine/memory.h"
#include "engine/sound_bank.h"
#include "engine/vehicle_sound.h"
#include "own_sound_ids.h"

#include <cstdint>
#include <span>

namespace {
    namespace OwnSounds = VehicleOverhaul::OwnSounds;
    using VehicleOverhaul::At;
    using Slots = std::span<const OwnSounds::Slot>;

    bool g_held = false;

    bool Own(uint32_t id) { return id >> 16 == 0x00FD; }

    // The overhaul's sounds into the slots, or the game's back.
    void Set(uint8_t* sound, Slots slots, bool own) {
        for (const OwnSounds::Slot& slot : slots) {
            At<uint32_t>(sound, slot.offset) = own ? slot.id : slot.game;
        }
    }

    // The car whose engine loop this is, the game's or the overhaul's.
    Slots CarOf(uint32_t engineLoop) {
        for (Slots car : OwnSounds::kCars) {
            for (const OwnSounds::Slot& slot : car) {
                if (slot.offset == OwnSounds::kEngineLoop && (slot.id == engineLoop || slot.game == engineLoop)) {
                    return car;
                }
            }
        }
        return {};
    }

    void BeforeUpdate(uint8_t* sound, bool playerIn) {
        if (!g_held) {
            g_held = true;
            for (uint32_t id : OwnSounds::kBanks) {
                VehicleOverhaul::SoundBank::Hold(id);
            }
        }
        const uint32_t engineLoop = At<uint32_t>(sound, OwnSounds::kEngineLoop);
        if (playerIn != Own(engineLoop) && !VehicleOverhaul::VehicleSound::EngineRunning(sound)) {
            Set(sound, CarOf(engineLoop), playerIn);
        }
        if (playerIn != Own(At<uint32_t>(sound, OwnSounds::kWheels[0].offset))) {
            Set(sound, OwnSounds::kWheels, playerIn);
        }
    }
}

namespace VehicleOverhaul::PlayerSounds {

bool Install() { return VehicleSound::BeforeUpdate(&BeforeUpdate); }

}
