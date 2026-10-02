// The guards' sight of the player while the flashlight is on.
//
// CSensorySystem::AdjustFOV sets one guard's two vision cones for one target. Hooked where it has
// finished, a lit flashlight gets two of the engine's own rules applied to the player: he keeps a
// fresh muzzle flash, which lifts the night's shorter cones, and a guard the beam reaches sees all
// around him, as one sitting in a vehicle does.
#include "engine/sight.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <iterator>

namespace {
    // AdjustFOV's muzzle-flash test: `mov eax, [timer]`, `fld [eax+30h]`, then the getter's call.
    FCSE::Relocation<uint8_t*> g_muzzleTest{
        FCSE::Pattern("A1 ?? ?? ?? ?? DD 40 30 8B 4F 08 D9 5C 24 28 E8 ?? ?? ?? ??")};
    // AdjustFOV past its range caps, where it asks whether the guard sits in a vehicle.
    FCSE::Relocation<uint8_t*> g_conesSet{FCSE::Pattern(
        "8B 45 10 8B 80 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 83 C4 04 84 C0 74 ?? 8B 4E 04 F3 0F 10 05 ?? ?? ?? ?? "
        "F3 0F 11 41 0C")};

    constexpr size_t kTimerGlobal = 0x01;
    constexpr size_t kGetterCall = 0x0F;
    // CPawnAgent::GetLastMuzzleFlashTime is `fld [ecx+<field>]`.
    constexpr uint8_t kFldEcx[] = {0xD9, 0x81};
    constexpr size_t kGetterField = 0x02;
    // The game timer's clock in seconds, the one the muzzle-flash time is kept in.
    constexpr ptrdiff_t kTimerNow = 0x30;

    // AdjustFOV's frame at the hook: whether its target is a player, and the target it was handed.
    constexpr ptrdiff_t kTargetIsPlayer = 0x0E;
    constexpr ptrdiff_t kVisualTarget = 0x28;
    constexpr ptrdiff_t kTargetAgent = 0x08;

    // CVisibilityContext: the cones being set, and the guard's head.
    constexpr ptrdiff_t kCones = 0x04;
    constexpr ptrdiff_t kHead = 0x10;
    // Each cone's length in metres and full angle in degrees.
    constexpr ptrdiff_t kFocusLength = 0x08;
    constexpr ptrdiff_t kFocusAngle = 0x0C;
    constexpr ptrdiff_t kPeripheralLength = 0x1C;
    constexpr ptrdiff_t kPeripheralAngle = 0x20;

    constexpr float kAllAround = 360.0f;
    // A cone this many times the guard's distance puts the player in its first quarter, which every
    // shipped archetype sees in full.
    constexpr float kReach = 4.0f;

    struct Beam {
        float origin[3];
        float direction[3];
        float range;
        float cosHalfAngle;
        bool lit;
    };

    // Set on the game's frame, read by every guard's vision update.
    std::atomic<Beam> g_beam{};

    uint8_t* const* g_timer = nullptr;
    int32_t g_flashTime = 0;

    template <typename T>
    T& Field(uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(object + offset);
    }

    // True when `head` is inside the beam.
    bool Lit(const Beam& beam, const float* head, float& distance) {
        float along = 0.0f;
        float squared = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const float offset = head[axis] - beam.origin[axis];
            along += offset * beam.direction[axis];
            squared += offset * offset;
        }
        distance = std::sqrt(squared);
        return distance <= beam.range && along >= beam.cosHalfAngle * distance;
    }

    void OnConesSet(FCSE_MidHookContext* ctx) {
        auto* frame = reinterpret_cast<uint8_t*>(ctx->esp);
        const Beam beam = g_beam.load();
        if (!beam.lit || Field<uint8_t>(frame, kTargetIsPlayer) == 0) {
            return;
        }

        uint8_t* player = Field<uint8_t*>(Field<uint8_t*>(frame, kVisualTarget), kTargetAgent);
        Field<float>(player, g_flashTime) = static_cast<float>(Field<double>(*g_timer, kTimerNow));

        auto* context = reinterpret_cast<uint8_t*>(ctx->esi);
        float distance = 0.0f;
        if (!Lit(beam, &Field<float>(context, kHead), distance)) {
            return;
        }
        uint8_t* cones = Field<uint8_t*>(context, kCones);
        const float reach = kReach * distance;
        Field<float>(cones, kFocusLength) = (std::max)(Field<float>(cones, kFocusLength), reach);
        Field<float>(cones, kPeripheralLength) = (std::max)(Field<float>(cones, kPeripheralLength), reach);
        Field<float>(cones, kFocusAngle) = kAllAround;
        Field<float>(cones, kPeripheralAngle) = kAllAround;
    }
}

namespace Flashlight::Sight {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    const uint8_t* test = g_muzzleTest.get();
    const uint8_t* call = test != nullptr ? test + kGetterCall : nullptr;
    const uint8_t* getter = call != nullptr ? call + 5 + *reinterpret_cast<const int32_t*>(call + 1) : nullptr;
    if (!g_conesSet || getter == nullptr || !std::equal(std::begin(kFldEcx), std::end(kFldEcx), getter)) {
        api->Log("sight: the guards' vision setup was not found in this build");
        return false;
    }

    g_timer = *reinterpret_cast<uint8_t* const* const*>(test + kTimerGlobal);
    g_flashTime = *reinterpret_cast<const int32_t*>(getter + kGetterField);
    return api->MidHook(g_conesSet.get(), &OnConesSet);
}

void SetBeam(const float* origin, const float* direction, float range, float outerAngle) {
    Beam beam{};
    std::copy_n(origin, 3, beam.origin);
    std::copy_n(direction, 3, beam.direction);
    beam.range = range;
    beam.cosHalfAngle = std::cos(outerAngle / 2.0f);
    beam.lit = true;
    g_beam.store(beam);
}

void Clear() { g_beam.store(Beam{}); }

}
