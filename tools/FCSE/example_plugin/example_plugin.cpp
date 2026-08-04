// example_plugin - minimal, self-contained FCSE plugin demonstrating all three tiers of the
// plugin API (see tools/FCSE/README.md). Meant as a copy-from starting point for real plugins as
// much as a smoke test for FCSE.exe itself.
#include "../include/plugin_api.h"

#include <windows.h>

namespace {
    const FCSE_PluginAPI* g_api = nullptr;

    // Tier 3 demo target: a small buffer inside this plugin's own module - never touches Dunia.dll
    // or any shared/system code, so this is always safe to run regardless of what's installed.
    unsigned char g_demoBuffer[4] = {0, 0, 0, 0};

    // Tier 2 demo target: kernel32's GetTickCount. Chosen because it's a trivial, extremely
    // well-known __stdcall export with no side effects to preserve beyond "return the real tick
    // count" - safe to detour as a mechanism demo without needing any Dunia.dll-specific RE work.
    // The detour is a transparent passthrough; the interesting log output is FCSE's own
    // install/conflict messages in hook.cpp, not anything this function does per call.
    using GetTickCountFn = DWORD(WINAPI*)();
    GetTickCountFn g_originalGetTickCount = nullptr;

    DWORD WINAPI GetTickCountDetour() {
        return g_originalGetTickCount();
    }

    // Tier 1 demo: overrides the stock "toRed" handler. Stock behavior writes 1 (normal RGB);
    // this plugin writes 0 instead, which docs/docs/engine-internals/function-registry.md and
    // reverse/patch_toRed.py confirm live in-game switches all 2D UI/HUD rendering to
    // red-channel-only. Needs zero Dunia.dll address knowledge - just claim the name before
    // FCSE's own stock registration runs (see debug_commands.cpp's Provider()).
    int __cdecl ToRedOverride(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = 0;
        return 0;
    }

    // Tier 4 demo target: one bool, shown as a row in the "Mods" tab under Options.
    bool g_demoBool = false;

    void __cdecl OnDemoBoolChanged(void* /*userdata*/) {
        if (g_api != nullptr) {
            g_api->Log(g_demoBool ? "example_plugin: demo bool toggled ON"
                                   : "example_plugin: demo bool toggled OFF");
        }
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    g_api = api;

    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // unsupported loader version - FCSE logs why, this plugin logs nothing yet
    }

    api->Log("example_plugin loaded");

    void* target = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "GetTickCount");
    if (api->Hook(target, reinterpret_cast<void*>(&GetTickCountDetour),
                  reinterpret_cast<void**>(&g_originalGetTickCount))) {
        api->Log("example_plugin: GetTickCount hook installed");
    }

    unsigned char patchBytes[4] = {1, 2, 3, 4};
    if (api->Patch(g_demoBuffer, patchBytes, sizeof(patchBytes))) {
        api->Log("example_plugin: demo buffer patched");
    }

    static FCSE_ConfigBool demoField{};
    demoField.label = "Demo bool";
    demoField.value = &g_demoBool;
    demoField.onChanged = &OnDemoBoolChanged;
    demoField.userdata = nullptr;
    if (api->RegisterConfigPage("example_plugin", &demoField, 1)) {
        api->Log("example_plugin: registered a Mods tab entry");
    }

    return true;
}

extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api) {
    // AddFunctionCB is void by design (it matches Dunia.dll's own AddFunctionCB signature exactly
    // - see plugin_api.h) - whether this claim actually won is only visible in fcse.log, via
    // FCSE's own function_registry.cpp logging, not through a return value here.
    api->AddFunctionCB(reinterpret_cast<void*>(&ToRedOverride), "toRed");
    api->Log("example_plugin: toRed registration attempted, see fcse.log for whether it won");
}
