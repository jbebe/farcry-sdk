// The guards' sight of the player while the flashlight is on. Hooked where CSensorySystem::AdjustFOV
// has set one guard's cones for one target, it keeps the player's muzzle flash fresh, and lets a
// guard inside the beam see all around him.
#include "engine/sight.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <initializer_list>

namespace {
    using Flashlight::Field;
    using Flashlight::Vec3;

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
    constexpr uint8_t kFld = 0xD9;
    constexpr uint8_t kEcxDisp32 = 0x81;
    constexpr size_t kGetterField = 0x02;
    // The game timer's clock, in seconds.
    constexpr ptrdiff_t kTimerNow = 0x30;

    // AdjustFOV's frame at the hook: whether its target is a player, and the target it was handed.
    constexpr ptrdiff_t kTargetIsPlayer = 0x0E;
    constexpr ptrdiff_t kVisualTarget = 0x28;
    constexpr ptrdiff_t kTargetAgent = 0x08;

    // CVisibilityContext: the cones being set, and the guard's head.
    constexpr ptrdiff_t kCones = 0x04;
    constexpr ptrdiff_t kHead = 0x10;
    // The focus and the peripheral cone, each a length in metres then a full angle in degrees.
    constexpr ptrdiff_t kFocusCone = 0x08;
    constexpr ptrdiff_t kPeripheralCone = 0x1C;
    constexpr ptrdiff_t kConeLength = 0x00;
    constexpr ptrdiff_t kConeAngle = 0x04;

    constexpr float kAllAround = 360.0f;
    // Cone length in multiples of the guard's distance: the player lands in the first quarter, which
    // every shipped archetype sees in full.
    constexpr float kReach = 4.0f;

    struct Beam {
        Flashlight::Light::Pose pose;
        float range;
        float cosHalfAngle;
        bool lit;
    };

    // Set on the game's frame, read by every guard's vision update.
    std::atomic<Beam> g_beam{};

    uint8_t* const* g_timer = nullptr;
    int32_t g_flashTime = 0;

    // Whether `head` is inside the beam, and how far from the lamp it is.
    bool Lit(const Beam& beam, const float* head, float& distance) {
        float along = 0.0f;
        float squared = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const float offset = head[axis] - beam.pose.position[axis];
            along += offset * beam.pose.direction[axis];
            squared += offset * offset;
        }
        distance = std::sqrt(squared);
        return distance <= beam.range && along >= beam.cosHalfAngle * distance;
    }

    void OnConesSet(FCSE_MidHookContext* ctx) {
        auto* frame = reinterpret_cast<uint8_t*>(ctx->esp);
        if (Field<uint8_t>(frame, kTargetIsPlayer) == 0) {
            return;
        }
        const Beam beam = g_beam.load();
        if (!beam.lit) {
            return;
        }

        uint8_t* player = Field<uint8_t*>(Field<uint8_t*>(frame, kVisualTarget), kTargetAgent);
        Field<float>(player, g_flashTime) = static_cast<float>(Field<double>(*g_timer, kTimerNow));

        auto* context = reinterpret_cast<uint8_t*>(ctx->esi);
        float distance = 0.0f;
        if (!Lit(beam, Vec3(context, kHead), distance)) {
            return;
        }
        uint8_t* cones = Field<uint8_t*>(context, kCones);
        for (const ptrdiff_t cone : {kFocusCone, kPeripheralCone}) {
            float& length = Field<float>(cones, cone + kConeLength);
            length = (std::max)(length, kReach * distance);
            Field<float>(cones, cone + kConeAngle) = kAllAround;
        }
    }
}

namespace Flashlight::Sight {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    const uint8_t* test = g_muzzleTest.get();
    const uint8_t* getter = test != nullptr ? CallTarget<const uint8_t*>(test + kGetterCall) : nullptr;
    if (!g_conesSet || getter == nullptr || getter[0] != kFld || getter[1] != kEcxDisp32) {
        api->Log("sight: the guards' vision setup was not found in this build");
        return false;
    }

    g_timer = *reinterpret_cast<uint8_t* const* const*>(test + kTimerGlobal);
    g_flashTime = *reinterpret_cast<const int32_t*>(getter + kGetterField);
    return api->MidHook(g_conesSet.get(), &OnConesSet);
}

void SetBeam(const Light::Pose& pose, const Light::Spot& spot) {
    g_beam.store({pose, spot.range, std::cos(spot.outerAngle / 2.0f), true});
}

void Clear() { g_beam.store(Beam{}); }

}
