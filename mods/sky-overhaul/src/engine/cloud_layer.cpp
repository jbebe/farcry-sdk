#include "engine/cloud_layer.h"

#include "engine/seqlock.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace {
    // Where each parameter read here sits inside the renderer's scene state, which the submission
    // is handed as its sixth argument.
    constexpr size_t kStorm = 0x78;
    constexpr size_t kSunDirection = 0x148;
    // The moon's direction in the world, which is where its sprite is drawn.
    constexpr size_t kMoonDirection = 0x194;
    constexpr size_t kMoonColour = 0x1A0;
    constexpr size_t kNight = 0x1B8;
    constexpr size_t kTimeOfDay = 0x1BC;
    constexpr size_t kWind = 0x1E0;
    constexpr size_t kAmbientColour = 0x230;

    // Twelve stack arguments, of which only the sixth is read; the rest are named to get the stack
    // shape right, since the callee cleans it. __fastcall stands in for __thiscall, which MSVC
    // will not let a free function be.
    using SubmitCloudsFn = void(__fastcall*)(void* self, void* unused, uint32_t a2, uint32_t a3,
                                             uint32_t a4, uint32_t a5, uint32_t a6,
                                             const uint8_t* state, uint32_t a8, uint32_t a9,
                                             uint32_t a10, uint32_t a11, uint32_t a12,
                                             uint32_t a13);

    // The address library carries this address, and resolving it that way is what proves the
    // translation on the build it was not read from. The pattern is the second opinion: the
    // prologue through both layer-enable tests, every byte of which is fixed.
    FCSE::Relocation<SubmitCloudsFn> g_mapped{FCSE::Uplay(0x003DC3C0)};
    FCSE::Relocation<SubmitCloudsFn> g_scanned{FCSE::Pattern(
        "55 8B EC 83 E4 F0 83 EC 64 53 56 8B 75 1C 80 BE DC 01 00 00 00 8B D9 57 89 5C 24 0C "
        "75 0D 80 BE FC 01 00 00 00")};

    SubmitCloudsFn g_original = nullptr;

    SkyOverhaul::Seqlock<SkyOverhaul::CloudLayer::Lighting> g_lighting;
    std::atomic<uint32_t> g_submitCount{0};

    // Written by the settings callback and read by the submission. A lone aligned value that no
    // other has to agree with, so a torn read is neither possible nor consequential.
    SkyOverhaul::CloudLayer::Mode g_mode = SkyOverhaul::CloudLayer::Mode::Engine;

    const float* Field(const uint8_t* state, size_t offset) {
        return reinterpret_cast<const float*>(state + offset);
    }

    void Copy3(const uint8_t* state, size_t offset, float* out) {
        const float* from = Field(state, offset);
        out[0] = from[0];
        out[1] = from[1];
        out[2] = from[2];
    }

    // Left as it is when it is too short to have a direction, which is what an unloaded world
    // reads as.
    void Normalise(float* direction) {
        const float length = std::sqrt(direction[0] * direction[0] + direction[1] * direction[1] +
                                       direction[2] * direction[2]);
        if (length > 0.0001f) {
            direction[0] /= length;
            direction[1] /= length;
            direction[2] /= length;
        }
    }

    void Read(const uint8_t* state, SkyOverhaul::CloudLayer::Lighting& out) {
        Copy3(state, kSunDirection, out.sunDirection);
        Normalise(out.sunDirection);

        Copy3(state, kMoonDirection, out.moonDirection);
        Normalise(out.moonDirection);

        Copy3(state, kMoonColour, out.moonColour);
        Copy3(state, kAmbientColour, out.ambientColour);

        const float* wind = Field(state, kWind);
        out.wind[0] = wind[0];
        out.wind[1] = wind[1];

        out.storm = *Field(state, kStorm);
        out.night = *Field(state, kNight);
        out.timeOfDay = *Field(state, kTimeOfDay);
    }

    void __fastcall SubmitCloudsDetour(void* self, void* unused, uint32_t a2, uint32_t a3,
                                       uint32_t a4, uint32_t a5, uint32_t a6, const uint8_t* state,
                                       uint32_t a8, uint32_t a9, uint32_t a10, uint32_t a11,
                                       uint32_t a12, uint32_t a13) {
        if (state != nullptr) {
            SkyOverhaul::CloudLayer::Lighting lighting = {};
            Read(state, lighting);
            g_lighting.Publish(lighting);
            g_submitCount.fetch_add(1, std::memory_order_relaxed);
        }

        // Returning here is what the engine itself does for a world with no cloud layers enabled:
        // the packets are never appended, so nothing downstream has anything to draw.
        if (g_mode == SkyOverhaul::CloudLayer::Mode::Engine) {
            g_original(self, unused, a2, a3, a4, a5, a6, state, a8, a9, a10, a11, a12, a13);
        }
    }
}

bool SkyOverhaul::CloudLayer::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const uintptr_t mapped = g_mapped.address();
    const uintptr_t scanned = g_scanned.address();
    if (mapped == 0 && scanned == 0) {
        api->Log("clouds unavailable: the cloud-layer submission was not found in this build");
        return false;
    }
    if (mapped != scanned) {
        FCSE::Logf("clouds: the address library says 0x%08zX and the pattern says 0x%08zX, "
                   "taking %s", static_cast<size_t>(mapped), static_cast<size_t>(scanned),
                   scanned != 0 ? "the pattern" : "the library");
    }

    // A disagreement goes to the pattern, which is checked against both shipped builds before
    // every release, where a translated address is only ever as good as the mapping behind it.
    const uintptr_t target = scanned != 0 ? scanned : mapped;

    if (!api->Hook(reinterpret_cast<void*>(target), reinterpret_cast<void*>(&SubmitCloudsDetour),
                   reinterpret_cast<void**>(&g_original))) {
        api->Log("clouds unavailable: the cloud-layer submission could not be hooked");
        return false;
    }

    FCSE::Logf("clouds: cloud-layer submission hooked at 0x%08zX on %s",
               static_cast<size_t>(target), api->gameBuildId);
    return true;
}

bool SkyOverhaul::CloudLayer::Latest(Lighting& out) {
    return g_lighting.Latest(out);
}

uint32_t SkyOverhaul::CloudLayer::SubmitCount() {
    return g_submitCount.load(std::memory_order_relaxed);
}

void SkyOverhaul::CloudLayer::SetMode(Mode mode) {
    g_mode = mode;
    FCSE::Logf("clouds: %s", mode == Mode::Engine ? "the engine draws its own clouds"
                                                  : "the engine's clouds are suppressed");
}
