// Every sound of the car the player drives, each to switch off and on from the window.
//
// A sound is switched off by its id, so it is kept from starting on every car that uses it. One already
// playing is stopped; a loop switched back on while the game means it to play is started again and handed
// to the vehicle, which stops it as it would its own.
#include "sounds.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "engine/pawn_tick.h"
#include "engine/sound_system.h"
#include "engine/vehicle.h"
#include "engine/vehicle_sound.h"
#include "engine/wheeled.h"

#include "imgui.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <iterator>

namespace {
    using VehicleOverhaul::At;
    using VehicleOverhaul::SoundSystem::kNone;
    namespace SoundSystem = VehicleOverhaul::SoundSystem;
    namespace VehicleSound = VehicleOverhaul::VehicleSound;

    constexpr size_t kSlotCount = std::size(VehicleSound::kSlots);

    // A small set of sound ids, written by the window and read anywhere.
    template <size_t N>
    struct IdSet {
        std::array<std::atomic<uint32_t>, N> ids;

        IdSet() {
            for (auto& id : ids) {
                id = kNone;
            }
        }
        bool Has(uint32_t id) const {
            for (const auto& entry : ids) {
                if (entry.load(std::memory_order_relaxed) == id) {
                    return true;
                }
            }
            return false;
        }
        void Add(uint32_t id) {
            for (auto& entry : ids) {
                uint32_t empty = kNone;
                if (entry.load() == id || entry.compare_exchange_strong(empty, id)) {
                    return;
                }
            }
        }
        void Remove(uint32_t id) {
            for (auto& entry : ids) {
                uint32_t found = id;
                entry.compare_exchange_strong(found, kNone);
            }
        }
    };

    IdSet<32> g_muted;
    // Sounds of the vehicles' sound type heard while the player drives, beyond their car's slots.
    IdSet<32> g_heard;

    bool g_installed = false;

    // The player's vehicle entity, from the input pass.
    std::atomic<void*> g_player{nullptr};

    // For the window: the sound ids in the player's car's slots, and the sound type it plays them as.
    std::array<std::atomic<uint32_t>, kSlotCount> g_slotIds;
    std::atomic<int32_t> g_type{-1};

    // On the game thread: the player's car's sound, and whether each slot is switched off there.
    uint8_t* g_sound = nullptr;
    std::array<bool, kSlotCount> g_silenced{};

    // The game's sound types, as its mix names them.
    constexpr const char* kTypeNames[SoundSystem::kTypeCount] = {
        "Ambiance_Generic", "Ambiance_Inside", "Dialog", "Buddy_Dialog", "Bark_NPC", "Ono_NPC", "Ono_Player",
        "Breath_Player", "Weapon_Player", "Weapon_NPC", "Explosion", "Effect_3D", "Effect_Non_Loc", "Vehicles",
        "Foley_NPC", "Foley_Player", "Music_Breifing", "Music_InGame", "Music_Memorable", "Radio", "Interface",
        "animalsounds", "Infamy_FX", "Ambiance_River", "Ambiance_Followers"};

    // While the player drives, the dB added to each sound type's volume from the game's mix; set from the
    // window, and counted so the game thread sends the mix again when it changes.
    std::array<std::atomic<float>, SoundSystem::kTypeCount> g_mix{};
    std::atomic<int> g_mixVersion{0};
    bool g_mixInstalled = false;
    // On the game thread: the version sent, and whether it was sent for a driver.
    int g_mixSent = -1;
    bool g_mixSentDriving = false;

    float AdjustType(int32_t type, float volume) {
        return g_player.load(std::memory_order_relaxed) != nullptr ? volume + g_mix[type].load(std::memory_order_relaxed)
                                                                   : volume;
    }

    void Tick(void* pawn, float*) {
        void* player = VehicleOverhaul::Wheeled::Player() != nullptr
                           ? VehicleOverhaul::Entity::Of(VehicleOverhaul::Vehicle::Current(pawn))
                           : nullptr;
        g_player = player;
        // Getting back in restarts the engine under the switches as they are then.
        if (player == nullptr) {
            g_sound = nullptr;
        }
        const bool driving = player != nullptr;
        const int version = g_mixVersion;
        if (g_mixInstalled && (driving != g_mixSentDriving || (driving && version != g_mixSent))) {
            g_mixSentDriving = driving;
            g_mixSent = version;
            SoundSystem::ResendTypeVolumes();
        }
    }

    bool OnStart(uint32_t id, int32_t type, const void*) {
        if (type == g_type.load(std::memory_order_relaxed) && g_player.load(std::memory_order_relaxed) != nullptr) {
            g_heard.Add(id);
        }
        return !g_muted.Has(id);
    }

    void Silence(uint8_t* sound, const VehicleSound::Slot& slot) {
        if (slot.handle != 0) {
            uint32_t& handle = At<uint32_t>(sound, slot.handle);
            VehicleOverhaul::SoundSystem::Stop(handle);
            handle = kNone;
        } else if (slot.stop != 0) {
            VehicleOverhaul::SoundSystem::Play(At<uint32_t>(sound, slot.stop), VehicleSound::Type(sound),
                                               VehicleSound::Callbacks(sound));
        }
    }

    void Resume(uint8_t* sound, const VehicleSound::Slot& slot) {
        if (slot.looping == 0 || At<uint8_t>(sound, slot.looping) == 0) {
            return;
        }
        const uint32_t handle = VehicleOverhaul::SoundSystem::Play(At<uint32_t>(sound, slot.id), VehicleSound::Type(sound),
                                                                   VehicleSound::Callbacks(sound));
        if (slot.handle != 0) {
            At<uint32_t>(sound, slot.handle) = handle;
        }
    }

    void Observe(void* vehicle, uint8_t* sound) {
        if (vehicle == nullptr || vehicle != g_player.load()) {
            return;
        }
        // A car just got into: the starts it made were already kept or let through as switched now.
        const bool fresh = sound != g_sound;
        g_sound = sound;
        g_type = VehicleSound::Type(sound);
        for (size_t i = 0; i < kSlotCount; ++i) {
            const VehicleSound::Slot& slot = VehicleSound::kSlots[i];
            const uint32_t id = At<uint32_t>(sound, slot.id);
            g_slotIds[i] = id;
            const bool silenced = id != kNone && g_muted.Has(id);
            if (!fresh && silenced != g_silenced[i]) {
                silenced ? Silence(sound, slot) : Resume(sound, slot);
            }
            g_silenced[i] = silenced;
        }
    }

    bool InSlots(uint32_t id) {
        for (const auto& slotId : g_slotIds) {
            if (slotId.load() == id) {
                return true;
            }
        }
        return false;
    }

    void Row(const char* label, uint32_t id) {
        ImGui::PushID(static_cast<int>(id));
        bool on = !g_muted.Has(id);
        if (ImGui::Checkbox(label, &on)) {
            on ? g_muted.Remove(id) : g_muted.Add(id);
        }
        ImGui::SameLine();
        ImGui::TextDisabled("0x%08X", id);
        ImGui::PopID();
    }
}

namespace VehicleOverhaul::Sounds {

bool Install() {
    for (auto& id : g_slotIds) {
        id = kNone;
    }
    VehicleSound::Observe(&Observe);
    g_installed = SoundSystem::Install(&OnStart) && PawnTick::Subscribe(&Tick);
    g_mixInstalled = g_installed && SoundSystem::AdjustTypeVolumes(&AdjustType);
    return g_installed;
}

void DrawTab() {
    if (!g_installed) {
        ImGui::TextDisabled("Unavailable in this build; fcse.log says why.");
        return;
    }
    if (g_player.load() == nullptr) {
        ImGui::TextDisabled("Not driving.");
        return;
    }
    ImGui::TextWrapped("The sounds of the car you drive. A sound switched off stays off on every car that "
                       "uses it, until it is switched back on or the game restarts.");

    for (size_t i = 0; i < kSlotCount; ++i) {
        const uint32_t id = g_slotIds[i];
        if (id == kNone) {
            ImGui::TextDisabled("%s: none", VehicleSound::kSlots[i].name);
        } else {
            Row(VehicleSound::kSlots[i].name, id);
        }
    }
    const uint32_t shift = VehicleSound::ShiftSound();
    if (shift != 0) {
        Row("Gear change (Vehicle Overhaul)", shift);
    }

    ImGui::SeparatorText("Other vehicle sounds heard");
    bool any = false;
    for (const auto& entry : g_heard.ids) {
        const uint32_t id = entry.load();
        if (id != kNone && id != shift && !InSlots(id)) {
            Row("##other", id);
            any = true;
        }
    }
    if (!any) {
        ImGui::TextDisabled("None yet.");
    }

    ImGui::Separator();
    if (ImGui::Button("All on")) {
        for (auto& id : g_muted.ids) {
            id = kNone;
        }
    }

    if (!g_mixInstalled) {
        return;
    }
    ImGui::SeparatorText("Mix while driving");
    ImGui::TextWrapped("Each kind of sound taken down while you drive, on top of the game's own mix. Not saved.");
    ImGui::PushItemWidth(-ImGui::GetFontSize() * 10.0f);
    for (int32_t type = 0; type < SoundSystem::kTypeCount; ++type) {
        float value = g_mix[type];
        if (ImGui::SliderFloat(kTypeNames[type], &value, -30.0f, 0.0f, "%.0f dB", ImGuiSliderFlags_AlwaysClamp)) {
            g_mix[type] = value;
            ++g_mixVersion;
        }
    }
    ImGui::PopItemWidth();
    if (ImGui::Button("Reset mix")) {
        for (auto& value : g_mix) {
            value = 0.0f;
        }
        ++g_mixVersion;
    }
}

}
