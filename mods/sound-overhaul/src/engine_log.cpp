// Engine log: ten times a second, the RPM, speed and gear a vehicle answers, and the volume and voice of each
// layer of the Datsun's engine multilayer, as lines in fcse.log; and the moment a layer's voice is lost for
// good. A diagnostic, to be removed.
#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>

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

    // A layer's voice handle points at its child instance, whose [5] is the sample instance; that one's first
    // field is its voice manager entry, with its state: 1 playing, 2 or 3 virtual, 4 stopped.
    constexpr uintptr_t kChildSample = 5 * sizeof(uintptr_t);
    constexpr uintptr_t kEntryState = 0x28;

    // A wheeled vehicle's fields: the parameter ids it answers, its emulated RPM and gear.
    constexpr uintptr_t kVehicle = 0xC;
    constexpr uintptr_t kSpeedId = 0x250;
    constexpr uintptr_t kRpmId = 0x254;
    constexpr uintptr_t kGear = 0x36C;
    // The gear table's first gear maximum speed: 5 m/s in retail, 4.5 in the mod.
    constexpr uintptr_t kFirstGearMaxSpeed = 0x348;

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
    float g_topSpeed = 0.0f;

    struct Engine {
        uintptr_t instance = 0;
        int layers = 0;
        std::array<float, kLayers> volume{};
        std::array<int, kLayers> handle{-1, -1, -1};
        std::array<int, kLayers> state{};
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
                g_topSpeed = *reinterpret_cast<const float*>(vehicle + kFirstGearMaxSpeed);
            } else if (parameter == *reinterpret_cast<const uint32_t*>(vehicle + kSpeedId)) {
                g_speed = value;
            }
        }
        return value;
    }

    // A multilayer instance's resource record: its id first, its layer count at +0x18.
    const uint8_t* Resource(uintptr_t instance) {
        const int index = *reinterpret_cast<const int*>(instance + kResourceIndex);
        const uint8_t* table = *reinterpret_cast<uint8_t* const*>(g_getProject() + 0x8);
        return *reinterpret_cast<const uint8_t* const*>(table + 4 + index * 0x14);
    }
    constexpr uintptr_t kLayerCount = 0x18;

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
        const uint8_t* resource = Resource(instance);
        if (*reinterpret_cast<const uint32_t*>(resource) != kEngineMultilayer) {
            return nullptr;
        }
        *stalest = Engine{instance};
        stalest->layers = (std::min)(*reinterpret_cast<const int*>(resource + kLayerCount), kLayers);
        return stalest;
    }

    const int* Handles(uintptr_t instance) {
        return *reinterpret_cast<const int* const*>(instance + kLayerHandles);
    }

    void OnPushToVoice(FCSE_MidHookContext* ctx) {
        Engine* engine = EngineOf(ctx->edi);
        const int layer = static_cast<int>(ctx->esi);
        if (engine == nullptr || layer < 0 || layer >= engine->layers) {
            return;
        }
        engine->volume[layer] = *reinterpret_cast<const int32_t*>(ctx->edx + kVolume) / 65536.0f;
        const auto sample = *reinterpret_cast<const uintptr_t*>(ctx->ecx + kChildSample);
        const auto entry = sample != 0 ? *reinterpret_cast<const uintptr_t*>(sample) : 0;
        engine->state[layer] = entry != 0 ? *reinterpret_cast<const int*>(entry + kEntryState) : 0;

        const int* handles = Handles(engine->instance);
        int last = -1;
        for (int i = 0; i < engine->layers; ++i) {
            engine->handle[i] = handles[i];
            if (handles[i] != -1) {
                last = i;
            }
        }
        const ULONGLONG now = GetTickCount64();
        if (layer != last || now - engine->logged < kIntervalMs) {
            return;
        }
        engine->logged = now;
        char line[512];
        int length = std::snprintf(line, sizeof(line), "engine %08X: rpm %5.0f  speed %5.1f m/s  gear %d (1st gear to "
                                   "%.1f m/s)", static_cast<unsigned>(engine->instance), g_rpm, g_speed, g_gear,
                                   g_topSpeed);
        for (int i = 0; i < engine->layers && length > 0 && length < static_cast<int>(sizeof(line)); ++i) {
            const char* state = engine->handle[i] == -1                        ? "none"
                                : engine->state[i] == 1                        ? "playing"
                                : engine->state[i] == 2 || engine->state[i] == 3 ? "VIRTUAL"
                                : engine->state[i] == 4                        ? "STOPPED"
                                                                               : "?";
            length += std::snprintf(line + length, sizeof(line) - length, " | layer %d %6.1f dB %s", i,
                                    engine->volume[i], state);
        }
        FCSE::Logf("%s", line);
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
