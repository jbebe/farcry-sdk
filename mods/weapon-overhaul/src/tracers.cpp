// A shot decides on its tracer in CWeaponFireBulletStrategy::ApplyDelayBullet, which first skips it
// for a shooter drawn in first person. The hook sits on that test: a weapon not listed jumps past
// the tracer, and a listed one past the test alone.
//
// See docs/docs/engine-internals/bullet-tracers.md.
#include "tracers.h"

#include "engine/entity_name.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace {
    constexpr std::string_view kTracerWeapons[] = {
        "weapons.Special.M249_Saw",
        "weapons.Special.PKM",
        "weapons.Primary.Dragunov",
        "weapons.Primary.AS50",
    };

    // The test of the shooter's first-person flag and the long jump past the tracer it takes, with
    // the fire strategy in EDI; and the strategy's weapon.
    FCSE::Relocation<uint8_t*> g_firstPersonTest{FCSE::Pattern(
        "F6 40 04 80 0F 85 ?? ?? ?? ?? 8B 87 68 01 00 00 85 C0 0F 84 ?? ?? ?? ?? 8B 4F 50")};
    constexpr ptrdiff_t kJump = 4;
    constexpr ptrdiff_t kJumpLength = 6;
    constexpr ptrdiff_t kStrategyWeapon = 0x40;

    std::atomic<bool> g_enabled{true};
    uintptr_t g_pastTest = 0;
    uintptr_t g_pastTracer = 0;

    bool LeavesTracers(uint8_t* weapon) {
        char name[64];
        WeaponOverhaul::EntityName::Read(weapon, name);
        return std::ranges::any_of(kTracerWeapons, [&](std::string_view archetype) {
            return WeaponOverhaul::EntityName::Is(name, archetype);
        });
    }

    void OnFirstPersonTest(FCSE_MidHookContext* ctx) {
        if (!g_enabled) {
            return;
        }
        uint8_t* weapon = *reinterpret_cast<uint8_t**>(ctx->edi + kStrategyWeapon);
        ctx->eip = LeavesTracers(weapon) ? g_pastTest : g_pastTracer;
    }
}

bool WeaponOverhaul::Tracers::Install() {
    if (!g_firstPersonTest) {
        FCSE::Logf("tracers: the shot's tracer test was not found, so tracers are the game's own");
        return false;
    }
    const uintptr_t jump = g_firstPersonTest.address() + kJump;
    g_pastTest = jump + kJumpLength;
    g_pastTracer = g_pastTest + *reinterpret_cast<const int32_t*>(jump + 2);
    if (!FCSE::ApiPointer()->MidHook(g_firstPersonTest.get(), &OnFirstPersonTest)) {
        FCSE::Logf("tracers: the shot's tracer test cannot be hooked, so tracers are the game's own");
        return false;
    }
    return true;
}

void WeaponOverhaul::Tracers::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
