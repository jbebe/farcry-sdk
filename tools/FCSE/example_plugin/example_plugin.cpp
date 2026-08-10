// example_plugin - a complete, working FCSE plugin in one file: two toggleable rendering effects,
// both reachable from the in-game Mod Configuration Menu, both surviving a restart.
//
//   Shake the UI    every menu, the HUD and the map jitter a few pixels each frame
//   Red UI          the entire 2D layer renders red-channel-only
//
// `example_script/example_script.lua` is the same mod written in Lua. The two files are meant to
// be read side by side - they use the same seams in the same order, so the comparison answers
// "what does the script API give up?" directly. (For this mod: nothing.)
//
// Copy this file as a starting point for a real plugin; `include/fcse_api.h` is the only header
// you need, and it documents the whole ABI inline.
#include "fcse_api.h"

#include <cstdint>
#include <cstdio>

namespace {
    const FCSE_PluginAPI* g_api = nullptr;

    // ==========================================================================================
    // Effect 1 - shake the UI (Tier 2: a hook on an engine method)
    // ==========================================================================================
    //
    // `magma::CRenderNomadImpl` is the vertex sink for Far Cry 2's entire 2D layer: HUD, map,
    // weapon wheel, and every menu. Each widget quad is emitted as BeginQuad -> SetVertex x4 ->
    // EndQuad, and EndQuad finishes each of the four corners with
    //
    //     x' = (x + m_originX) / m_width * m_scaleX + m_biasX
    //     y' = (y + m_originY) / m_height
    //
    // so m_originX/m_originY are a pixel-space translation added to every vertex the UI draws.
    // `BeginPageRendering()` refills both from the current viewport at the start of every page,
    // which is what makes them a good thing for a mod to touch: adding a random offset just after
    // it runs shakes the whole interface, and switching the effect off restores the game exactly,
    // because the engine overwrites both fields again on the very next frame. Nothing is saved,
    // nothing is unpatched, and a crash mid-shake leaves no trace on disk.
    constexpr uintptr_t kOriginX = 0xE0; // float, magma::CRenderNomadImpl
    constexpr uintptr_t kOriginY = 0xE4; // float, the next one along

    // Screen pixels - m_originX/Y are in viewport pixels, so this is literal distance.
    constexpr float kShakeAmplitude = 6.0f;

    bool g_shakeEnabled = false;

    // `magma::CRenderNomadImpl::BeginPageRendering`, named as it appears in Steam's Dunia.dll.
    // Naming it from GOG's instead - FCSE::Retail(0x005ED140) - is the same function and resolves
    // just as correctly on either build; only one of the two is ever needed. Note the two numbers
    // are nothing like each other, and no arithmetic turns one into the other: only the lookup
    // does. FCSE::Pattern("..") is the third way, for builds the address library has never seen.
    //
    // The method is __thiscall (`this` in ECX, no stack arguments). MSVC will not let a free
    // function be declared __thiscall, so the detour below is __fastcall with an unused second
    // parameter - for a method that takes nothing, the two are the same ABI: ECX carries `this`,
    // EDX is ignored, and neither convention cleans any stack.
    using BeginPageRenderingFn = void(__fastcall*)(void* self, void* unused);

    FCSE::Relocation<BeginPageRenderingFn> g_beginPageRendering{FCSE::Uplay(0x005FA9C0)};
    BeginPageRenderingFn g_originalBeginPageRendering = nullptr;

    // A private generator rather than rand(): the game uses the CRT's global one, and stepping it
    // from a render callback on every frame would quietly shift the game's own random sequence.
    // A mod should not have side effects it did not ask for.
    uint32_t g_rng = 0x9E3779B9u;

    float NextJitter() {
        g_rng ^= g_rng << 13;
        g_rng ^= g_rng >> 17;
        g_rng ^= g_rng << 5;
        // Top 24 bits as [0,1), then rescaled to [-kShakeAmplitude, +kShakeAmplitude].
        const float unit = static_cast<float>(g_rng >> 8) * (1.0f / 16777216.0f);
        return (unit * 2.0f - 1.0f) * kShakeAmplitude;
    }

    void __fastcall BeginPageRenderingDetour(void* self, void* unused) {
        // Let the engine set the real viewport origin first, then perturb it. Doing it in this
        // order is what makes the effect self-restoring - the value we modify is written fresh
        // every frame, so we are never responsible for putting anything back.
        g_originalBeginPageRendering(self, unused);

        if (!g_shakeEnabled || self == nullptr) {
            return;
        }

        // A direct store, deliberately *not* api->Patch(): Patch is for editing code and static
        // data once, and it logs and overlap-checks every call. This is a per-frame write into a
        // live engine object, which is ordinary memory this plugin is already allowed to touch.
        auto* base = static_cast<char*>(self);
        *reinterpret_cast<float*>(base + kOriginX) += NextJitter();
        *reinterpret_cast<float*>(base + kOriginY) += NextJitter();
    }

    void __cdecl OnShakeChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
        g_shakeEnabled = value->asCheckbox;
        if (g_api != nullptr) {
            g_api->Log(g_shakeEnabled ? "example_plugin: UI shake is ON"
                                      : "example_plugin: UI shake is OFF");
        }
    }

    // ==========================================================================================
    // Effect 2 - red UI (Tier 1: claiming one of Dunia's named callbacks)
    // ==========================================================================================
    //
    // The cheapest real hook there is: no address at all, just a name claimed before FCSE's own
    // stock registration runs (see the loader's debug_commands.cpp). `FarCry2.exe` ships a "toRed"
    // handler that writes 1; the engine calls it once from
    // magma::CRenderNomadImpl::BeginRendering and keeps the answer in the renderer's "full colour"
    // field. Writing 0 instead makes the whole 2D layer render red-channel-only.
    //
    // Because the name is a string key rather than an address, this half of the plugin needs no
    // address library, no pattern, and no per-build knowledge whatsoever.
    bool g_redEnabled = false;

    int __cdecl ToRedOverride(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = g_redEnabled ? 0 : 1;
        return 0;
    }

    void __cdecl OnRedChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
        g_redEnabled = value->asCheckbox;
        if (g_api != nullptr) {
            g_api->Log(g_redEnabled ? "example_plugin: red UI is ON"
                                    : "example_plugin: red UI is OFF");
        }
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    g_api = api;

    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // unsupported loader version - FCSE logs why, this plugin logs nothing yet
    }

    api->Log("example_plugin loaded");

    // Wires up the address library behind FCSE::Relocation. Without it every Relocation stays
    // unresolved, which is why this is the first thing FCSE_Load does.
    if (!FCSE::Bind(api)) {
        api->Log("example_plugin: this FCSE predates the address library (API v5) - the UI shake "
                 "needs it, so only the red-UI effect will work");
    } else {
        char line[192];
        std::snprintf(line, sizeof(line), "example_plugin: game build %s, address mapping v%s",
                      api->gameBuildId, api->addressMapping);
        api->Log(line);

        // A missing address must never become a jump: check once, here, and disable the feature
        // rather than discovering it halfway through a frame.
        if (!g_beginPageRendering) {
            api->Log("example_plugin: BeginPageRendering is not mapped on this build - UI shake "
                     "disabled");
        } else if (api->Hook(reinterpret_cast<void*>(g_beginPageRendering.address()),
                             reinterpret_cast<void*>(&BeginPageRenderingDetour),
                             reinterpret_cast<void**>(&g_originalBeginPageRendering))) {
            std::snprintf(line, sizeof(line),
                          "example_plugin: hooked magma::CRenderNomadImpl::BeginPageRendering at "
                          "0x%08zX",
                          static_cast<size_t>(g_beginPageRendering.address()));
            api->Log(line);
        }
        // Hook() having failed is already logged by FCSE, naming the plugin that won the address.
        // g_originalBeginPageRendering stays null in that case, which is exactly why the detour is
        // never reached: it is only ever called through the hook that failed to install.
    }

    // Tier 4. Each callback fires once from inside this call carrying whatever bin\fcse.ini holds,
    // so both flags already reflect the player's saved choices by the time RegisterSettings
    // returns - there is no separate "read my config" step - and again on every in-game toggle.
    //
    // Both rows are checkboxes; a plugin can also declare Choice, Slider and Text rows, which take
    // their extra configuration from the fields after `userdata` (see FCSE_Setting in fcse_api.h).
    static const FCSE_Setting settings[] = {
        {"Shake the UI", FCSE_CHECKBOX(false), &OnShakeChanged, nullptr},
        {"Red UI", FCSE_CHECKBOX(false), &OnRedChanged, nullptr},
    };
    if (api->RegisterSettings("example_plugin", settings,
                              sizeof(settings) / sizeof(settings[0]))) {
        api->Log("example_plugin: registered its Mod Configuration Menu entries");
    }

    return true;
}

extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api) {
    // AddFunctionCB is void by design - it matches Dunia.dll's own AddFunctionCB signature exactly
    // (see fcse_api.h) - so whether this claim actually won is only visible in fcse.log, via
    // FCSE's own function_registry.cpp logging, not through a return value here.
    api->AddFunctionCB(reinterpret_cast<void*>(&ToRedOverride), "toRed");
    api->Log("example_plugin: toRed registration attempted, see fcse.log for whether it won");
}
