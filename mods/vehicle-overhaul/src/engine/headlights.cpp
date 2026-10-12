#include "engine/headlights.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <mutex>

namespace {
    using VehicleOverhaul::At;
    using VehicleOverhaul::Headlights::kMaxLamps;
    using VehicleOverhaul::Headlights::Lamp;
    using VehicleOverhaul::Headlights::Snapshot;

    // CVehicleTypeWheeled's CVehicle.
    constexpr ptrdiff_t kTypeVehicle = 0x04;

    // CVehicle's data, and its CVehicleLight headlamps.
    constexpr ptrdiff_t kVehicleData = 0x10;
    constexpr ptrdiff_t kDataHeadlamps = 0x54;
    constexpr ptrdiff_t kDataHeadlampCount = 0x58;

    // CVehicleLight: the bone its unlit part hangs from, and its state.
    constexpr ptrdiff_t kLampBone = 0x00;
    constexpr ptrdiff_t kLampState = 0x14;
    constexpr ptrdiff_t kLampStride = 0x18;
    // Lit on an AI's car, with its night part showing, and lit on the player's, which casts the light.
    constexpr int32_t kLitByAi = 2;
    constexpr int32_t kLitByPlayer = 3;

    // How many frames a lamp outlives the last update of its car, which the game can skip a frame.
    constexpr uint32_t kKeepFrames = 4;

    using UpdateFn = void(__fastcall*)(uint8_t* type, void* unused, float seconds);

    // CVehicleTypeWheeled::UpdateHeadlights.
    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x001B0850)};
    UpdateFn g_originalUpdate = nullptr;

    struct Tracked {
        Lamp lamp;
        uint32_t seen;
    };

    std::mutex g_mutex;
    Tracked g_tracked[kMaxLamps];
    int g_count = 0;
    // The frame what is read now will be posted as.
    uint32_t g_reading = 1;

    Snapshot g_posted[2] = {};

    void Track(const Lamp& lamp) {
        Tracked* found = std::find_if(g_tracked, g_tracked + g_count,
                                      [&](const Tracked& t) { return t.lamp.id == lamp.id; });
        if (found == g_tracked + g_count) {
            if (g_count == kMaxLamps) {
                return;
            }
            g_count++;
        }
        *found = {lamp, g_reading};
    }

    void Read(uint8_t* type) {
        void* vehicle = At<void*>(type, kTypeVehicle);
        void* entity = VehicleOverhaul::Entity::Of(vehicle);
        if (entity == nullptr) {
            return;
        }
        const uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
        const uint8_t* lamps = At<uint8_t*>(data, kDataHeadlamps);
        const uint32_t count = At<uint32_t>(data, kDataHeadlampCount);

        Lamp read[kMaxLamps];
        int lit = 0;
        for (uint32_t i = 0; i < count && lit < kMaxLamps; i++) {
            const uint8_t* lamp = lamps + i * kLampStride;
            const int32_t state = At<int32_t>(lamp, kLampState);
            if (state != kLitByAi && state != kLitByPlayer) {
                continue;
            }
            Lamp& out = read[lit];
            out.id = reinterpret_cast<uintptr_t>(lamp);
            if (VehicleOverhaul::Entity::Bone(entity, At<uint32_t>(lamp, kLampBone), out.position, out.forward)) {
                lit++;
            }
        }

        std::lock_guard<std::mutex> lock(g_mutex);
        for (int i = 0; i < lit; i++) {
            Track(read[i]);
        }
    }

    void __fastcall UpdateDetour(uint8_t* type, void* unused, float seconds) {
        g_originalUpdate(type, unused, seconds);
        Read(type);
    }
}

namespace VehicleOverhaul::Headlights {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update) {
        api->Log("headlights: CVehicleTypeWheeled::UpdateHeadlights was not found in this build");
        return false;
    }
    return api->Hook(reinterpret_cast<void*>(g_update.address()), reinterpret_cast<void*>(&UpdateDetour),
                     reinterpret_cast<void**>(&g_originalUpdate));
}

void Post(uint32_t frame) {
    std::lock_guard<std::mutex> lock(g_mutex);
    Snapshot& out = g_posted[frame % 2];
    out.lamps = 0;
    int kept = 0;
    for (int i = 0; i < g_count; i++) {
        if (g_tracked[i].seen + kKeepFrames < g_reading) {
            continue;
        }
        g_tracked[kept++] = g_tracked[i];
        out.lamp[out.lamps++] = g_tracked[i].lamp;
    }
    g_count = kept;
    g_reading = frame + 1;
}

const Snapshot& Of(uint32_t frame) {
    return g_posted[frame % 2];
}

}
