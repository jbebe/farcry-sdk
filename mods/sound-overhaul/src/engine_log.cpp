// Engine log: ten times a second, the RPM, speed and gear a vehicle answers, and the volume and voice of each
// layer of the Datsun's engine multilayer, as lines in fcse.log; and the moment a layer's voice is lost for
// good. A diagnostic, to be removed.
#include "fcse_api.h"

#include <windows.h>

#include <array>
#include <cstdint>

namespace {
    // The Datsun's sndEngineLoop multilayer.
    constexpr uint32_t kEngineMultilayer = 0x0045CBB4;
    constexpr ULONGLONG kIntervalMs = 100;

    // CVehicleTypeWheeledSoundCB::GetMultiLayer(parameter id).
    FCSE::Relocation<uint8_t*> g_getMultiLayer{FCSE::Pattern(
        "83 EC 0C 55 8B E9 8B 4D 0C 85 C9 0F 84 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B C8 85 C9 8B 45 0C 89 4C 24 08")};

    // The multilayer update's `mov ecx, [ebp+0xc]` before it pushes a layer's parameters to its voice; the
    // voice's `call` at +0x18 and the `mov [edx+esi*4], -1` that forgets a lost voice at +0x27.
    FCSE::Relocation<uint8_t*> g_layerUpdate{FCSE::Pattern(
        "8B 4D 0C 8B 7C 24 14 8B 47 04 8B 74 24 10 51 8B 0C B0 8D 54 24 24 52 51 E8 ?? ?? ?? ?? 83 C4 0C "
        "85 C0 75 0C 8B 57 04 C7 04 B2 FF FF FF FF")};
    constexpr uintptr_t kPushToVoice = 0x18;
    constexpr uintptr_t kForgetVoice = 0x27;

    // The multilayer update's start, whose first call fetches DARE's project.
    FCSE::Relocation<uint8_t*> g_multilayerUpdate{FCSE::Pattern(
        "55 8B EC 83 E4 F8 81 EC CC 00 00 00 D9 EE 53 D9 54 24 38 56 D9 54 24 40 57 D9 54 24 48 8B F9")};
    constexpr uintptr_t kGetProjectCall = 0x35;

    // A wheeled vehicle's fields: the parameter ids it answers, its emulated RPM and gear.
    constexpr uintptr_t kVehicle = 0xC;
    constexpr uintptr_t kSpeedId = 0x250;
    constexpr uintptr_t kRpmId = 0x254;
    constexpr uintptr_t kGear = 0x36C;

    // A multilayer instance: its resource index, and one voice handle per layer (-1 when it has none).
    constexpr uintptr_t kResourceIndex = 0x0;
    constexpr uintptr_t kLayerHandles = 0x4;
    // The layer parameters pushed to a voice: volume in dB, Q16.16.
    constexpr uintptr_t kVolume = 0x18;
    constexpr int kLayers = 3;

    using GetMultiLayerFn = float(__thiscall*)(void* callbacks, uint32_t parameter);
    using GetProjectFn = uint8_t* (*)();

    GetMultiLayerFn g_original = nullptr;
    GetProjectFn g_getProject = nullptr;

    float g_rpm = 0.0f;
    float g_speed = 0.0f;
    int g_gear = -1;

    struct Engine {
        uintptr_t instance = 0;
        std::array<float, kLayers> volume{};
        ULONGLONG logged = 0;
    };
    std::array<Engine, 4> g_engines;

    float __fastcall OnGetMultiLayer(void* callbacks, void*, uint32_t parameter) {
        const float value = g_original(callbacks, parameter);
        const auto vehicle = *reinterpret_cast<const uint8_t* const*>(static_cast<uint8_t*>(callbacks) + kVehicle);
        if (vehicle != nullptr) {
            if (parameter == *reinterpret_cast<const uint32_t*>(vehicle + kRpmId)) {
                g_rpm = value;
                g_gear = *reinterpret_cast<const int*>(vehicle + kGear);
            } else if (parameter == *reinterpret_cast<const uint32_t*>(vehicle + kSpeedId)) {
                g_speed = value;
            }
        }
        return value;
    }

    uint32_t ResourceId(uintptr_t instance) {
        const int index = *reinterpret_cast<const int*>(instance + kResourceIndex);
        const uint8_t* table = *reinterpret_cast<uint8_t* const*>(g_getProject() + 0x8);
        return **reinterpret_cast<const uint32_t* const*>(table + 4 + index * 0x14);
    }

    // The engine this multilayer instance is, taking the longest-silent slot for a new one; null for any
    // other multilayer.
    Engine* EngineOf(uintptr_t instance) {
        Engine* stalest = &g_engines[0];
        for (Engine& engine : g_engines) {
            if (engine.instance == instance) {
                return &engine;
            }
            if (engine.logged < stalest->logged) {
                stalest = &engine;
            }
        }
        if (ResourceId(instance) != kEngineMultilayer) {
            return nullptr;
        }
        *stalest = Engine{instance};
        return stalest;
    }

    const int* Handles(uintptr_t instance) {
        return *reinterpret_cast<const int* const*>(instance + kLayerHandles);
    }

    void OnPushToVoice(FCSE_MidHookContext* ctx) {
        Engine* engine = EngineOf(ctx->edi);
        const int layer = static_cast<int>(ctx->esi);
        if (engine == nullptr || layer < 0 || layer >= kLayers) {
            return;
        }
        engine->volume[layer] = *reinterpret_cast<const int32_t*>(ctx->edx + kVolume) / 65536.0f;

        const int* handles = Handles(engine->instance);
        int last = -1;
        for (int i = 0; i < kLayers; ++i) {
            if (handles[i] != -1) {
                last = i;
            }
        }
        const ULONGLONG now = GetTickCount64();
        if (layer != last || now - engine->logged < kIntervalMs) {
            return;
        }
        engine->logged = now;
        FCSE::Logf("engine %08X: rpm %5.0f  speed %5.1f m/s  gear %d | low %6.1f dB %s | medium %6.1f dB %s | "
                   "max %6.1f dB %s", static_cast<unsigned>(engine->instance), g_rpm, g_speed, g_gear,
                   engine->volume[0], handles[0] == -1 ? "NO VOICE" : "voice", engine->volume[1],
                   handles[1] == -1 ? "NO VOICE" : "voice", engine->volume[2],
                   handles[2] == -1 ? "NO VOICE" : "voice");
    }

    void OnForgetVoice(FCSE_MidHookContext* ctx) {
        Engine* engine = EngineOf(ctx->edi);
        if (engine != nullptr) {
            FCSE::Logf("engine %08X: layer %d lost its voice at %.1f dB (rpm %.0f)",
                       static_cast<unsigned>(engine->instance), static_cast<int>(ctx->esi),
                       engine->volume[ctx->esi % kLayers], g_rpm);
        }
    }

    const uint8_t* CallTarget(const uint8_t* call) {
        return call + 5 + *reinterpret_cast<const int32_t*>(call + 1);
    }
}

void InstallEngineLog() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_getMultiLayer || !g_layerUpdate || !g_multilayerUpdate) {
        api->Log("engine log: the vehicle or multilayer code was not found in this build");
        return;
    }
    g_getProject = reinterpret_cast<GetProjectFn>(
        const_cast<uint8_t*>(CallTarget(g_multilayerUpdate.get() + kGetProjectCall)));

    api->Hook(g_getMultiLayer.get(), reinterpret_cast<void*>(&OnGetMultiLayer),
              reinterpret_cast<void**>(&g_original));
    api->MidHook(g_layerUpdate.get() + kPushToVoice, &OnPushToVoice);
    api->MidHook(g_layerUpdate.get() + kForgetVoice, &OnForgetVoice);
    api->Log("engine log: logging the Datsun's engine layers");
}
