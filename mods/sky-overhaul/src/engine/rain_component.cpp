#include "engine/rain_component.h"

#include "engine/locate.h"
#include "engine/seqlock.h"
#include "fcse_api.h"

#include <atomic>
#include <cstddef>
#include <cstdint>

namespace {
    // CRainComponent.
    constexpr size_t kStrength = 0x10;
    constexpr size_t kStarted = 0x5D;
    constexpr size_t kDirection = 0xDC;

    using SetStrengthFn = void(__fastcall*)(uint8_t* self, void* unused, float strength);
    using SplashesFn = void(__fastcall*)(uint8_t* self, void* unused, float seconds);

    FCSE::Relocation<SetStrengthFn> g_setStrengthMapped{FCSE::Uplay(0x00050270)};
    FCSE::Relocation<SetStrengthFn> g_setStrengthScanned{FCSE::Pattern(
        "F3 0F 10 44 24 04 56 8B F1 57 F3 0F 11 46 10 8B 0D ?? ?? ?? ?? 8D 7E 24 57 E8 ?? ?? ?? ?? "
        "84 C0 75 1D F3 0F 10 46 10 8B CF")};
    // GenerateSplashes, RET 4.
    FCSE::Relocation<SplashesFn> g_splashesMapped{FCSE::Uplay(0x00054070)};
    FCSE::Relocation<SplashesFn> g_splashesScanned{FCSE::Pattern(
        "A1 ?? ?? ?? ?? DB 80 B0 03 00 00 83 EC 08 53 56 8B F1 8B 88 B0 03 00 00 85 C9 7D 06 D8 05")};

    SetStrengthFn g_original = nullptr;
    SplashesFn g_originalSplashes = nullptr;
    std::atomic<bool> g_hidden{false};
    SkyOverhaul::Seqlock<SkyOverhaul::RainComponent::State> g_state;

    float& Float(uint8_t* base, size_t offset) {
        return *reinterpret_cast<float*>(base + offset);
    }

    // The drops' particles take the strength they are handed here; the component's own is put back
    // after.
    void __fastcall SetStrengthDetour(uint8_t* self, void* unused, float strength) {
        const bool hidden = g_hidden.load(std::memory_order_relaxed);
        g_original(self, unused, hidden ? 0.0f : strength);
        Float(self, kStrength) = strength;

        SkyOverhaul::RainComponent::State state = {};
        state.strength = strength;
        for (int i = 0; i < 3; i++) {
            state.direction[i] = Float(self, kDirection + 4 * i);
        }
        state.started = self[kStarted] != 0;
        g_state.Publish(state);
    }

    // The game's splashes stay off while ours land; those already splashing finish.
    void __fastcall SplashesDetour(uint8_t* self, void* unused, float seconds) {
        if (!g_hidden.load(std::memory_order_relaxed)) {
            g_originalSplashes(self, unused, seconds);
        }
    }

    template <class Fn>
    bool HookSite(const char* what, uintptr_t mapped, uintptr_t scanned, Fn detour, Fn& original) {
        return SkyOverhaul::HookAt(SkyOverhaul::Locate(what, mapped, scanned), detour, original,
                                   "rain: %s was not found in this build - the game's own stay", what);
    }
}

bool SkyOverhaul::RainComponent::Install() {
    if (!HookSite("the rain's strength setter", g_setStrengthMapped.address(),
                  g_setStrengthScanned.address(), &SetStrengthDetour, g_original)) {
        return false;
    }
    HookSite("the rain's splashes", g_splashesMapped.address(), g_splashesScanned.address(),
             &SplashesDetour, g_originalSplashes);
    return true;
}

void SkyOverhaul::RainComponent::SetDropsHidden(bool hidden) {
    g_hidden.store(hidden, std::memory_order_relaxed);
}

bool SkyOverhaul::RainComponent::Latest(State& out) {
    return g_state.Latest(out);
}
