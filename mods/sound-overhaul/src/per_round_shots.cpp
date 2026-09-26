// Per-round shots: a full-auto weapon whose data empties both fire loops and sets a single shot plays
// that shot on every round, the way a single-shot weapon does.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // ApplyDelayBullet's `test al, al` on IsSingleShot, before it plays the single shot from the fire
    // properties at [EDI+0x50] (the `mov ecx, [ecx+0x454]` at the end).
    FCSE::Relocation<uint8_t*> g_singleShotTest{FCSE::Pattern(
        "84 C0 0F 84 ?? ?? ?? ?? 8B 4C 24 ?? E8 ?? ?? ?? ?? F6 40 04 80 0F 84 ?? ?? ?? ?? E8 ?? ?? ?? ?? "
        "D9 EE 8B 4F 50 8B 10 8B 92 9C 00 00 00 51 D9 1C 24 8D B7 24 01 00 00 56 8B B1 58 04 00 00 "
        "8B 89 54 04 00 00")};

    constexpr uintptr_t kFireProperties = 0x50;

    // Sound ids in CWeaponFireBulletProperties.
    constexpr uintptr_t kSingleShot = 0x454;
    constexpr uintptr_t kStartAuto = 0x45C;
    constexpr uintptr_t kThirdPersonSingleShot = 0x47C;
    constexpr uintptr_t kThirdPersonStartAuto = 0x480;
    constexpr uint32_t kNoSound = 0xFFFFFFFF;

    uint32_t SoundAt(uintptr_t properties, uintptr_t offset) {
        return *reinterpret_cast<const uint32_t*>(properties + offset);
    }

    void OnSingleShotTest(FCSE_MidHookContext* ctx) {
        if ((ctx->eax & 0xFF) != 0) {
            return;
        }

        const uintptr_t properties = *reinterpret_cast<const uintptr_t*>(ctx->edi + kFireProperties);
        const bool noLoops = SoundAt(properties, kStartAuto) == kNoSound &&
                             SoundAt(properties, kThirdPersonStartAuto) == kNoSound;
        const bool hasShot = SoundAt(properties, kSingleShot) != kNoSound ||
                             SoundAt(properties, kThirdPersonSingleShot) != kNoSound;
        if (noLoops && hasShot) {
            ctx->eax |= 1;
        }
    }
}

void ApplyPerRoundShots() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_singleShotTest) {
        api->Log("per-round shots: ApplyDelayBullet's single-shot test was not found in this build");
        return;
    }

    if (api->MidHook(g_singleShotTest.get(), &OnSingleShotTest)) {
        api->Log("per-round shots: full-auto weapons without fire loops play their single shot per round");
    }
}
