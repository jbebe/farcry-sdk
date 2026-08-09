// example_plugin - minimal, self-contained FCSE plugin demonstrating all three tiers of the
// plugin API (see tools/FCSE/README.md). Meant as a copy-from starting point for real plugins as
// much as a smoke test for FCSE.exe itself.
#include "plugin_api.h"

#include <cstdio>
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
    // docs/docs/engine-internals/function-registry.md and reverse/patch_toRed.py confirm live
    // in-game that writing 0 instead switches all 2D UI/HUD rendering to red-channel-only. "toRed"
    // is called every frame (FunctionRegistry_Invoke's normal dispatch), so this override just
    // reads g_toRedEnabled fresh each call - no re-hooking needed to react to the Tier 4 checkbox
    // below toggling it live. Needs zero Dunia.dll address knowledge - just claim the name before
    // FCSE's own stock registration runs (see debug_commands.cpp's Provider()).
    bool g_toRedEnabled = false;

    int __cdecl ToRedOverride(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = g_toRedEnabled ? 0 : 1;
        return 0;
    }

    // Tier 4 demo target: one Checkbox, persisted in bin\fcse.ini under [example_plugin] and shown
    // as a row in FCSE's Mod Configuration Menu. FCSE owns the stored value, so this callback is
    // the only way the plugin learns it - which is also what makes the setting survive a restart:
    // it fires once during FCSE_Load carrying whatever the file held, and again on every toggle.
    void __cdecl OnToRedChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
        g_toRedEnabled = value->asCheckbox;
        if (g_api != nullptr) {
            g_api->Log(g_toRedEnabled ? "example_plugin: toRed is ON" : "example_plugin: toRed is OFF");
        }
    }

    // The other three setting types, present only to demonstrate what a row of each looks like and
    // to give the menu something to exercise. None of them drives anything in this plugin.
    const char* const kVerbosityChoices[] = {"Quiet", "Normal", "Verbose"};

    void __cdecl OnDemoSettingChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
        if (g_api == nullptr) {
            return;
        }
        char message[160];
        switch (value->type) {
        case FCSE_SettingType_Choice:
            sprintf_s(message, "example_plugin: verbosity is %s",
                      kVerbosityChoices[value->asChoice]);
            break;
        case FCSE_SettingType_Slider:
            sprintf_s(message, "example_plugin: demo slider is %d", value->asSlider);
            break;
        case FCSE_SettingType_Text:
            sprintf_s(message, "example_plugin: demo text is \"%s\"",
                      value->asText != nullptr ? value->asText : "");
            break;
        default:
            return;
        }
        g_api->Log(message);
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

    // OnToRedChanged fires before RegisterSettings returns, so g_toRedEnabled already reflects
    // fcse.ini by the time this call is done - no separate "read my config" step.
    //
    // One row of each type, so the Mod Configuration Menu has something of every shape to show.
    // Everything after `userdata` is per-type configuration and is ignored by the types that do not
    // use it, which is why the Checkbox line does not have to mention any of it.
    static const FCSE_Setting settings[] = {
        {"Toggle toRed", FCSE_CHECKBOX(false), &OnToRedChanged, nullptr},
        {"Log verbosity", FCSE_CHOICE(1), &OnDemoSettingChanged, nullptr, kVerbosityChoices, 3},
        {"Demo slider", FCSE_SLIDER(5), &OnDemoSettingChanged, nullptr, nullptr, 0, 0, 10},
        {"Demo text", FCSE_TEXT(), &OnDemoSettingChanged, nullptr, nullptr, 0, 0, 0, "kilimanjaro",
         24},
    };
    if (api->RegisterSettings("example_plugin", settings,
                              sizeof(settings) / sizeof(settings[0]))) {
        api->Log("example_plugin: registered its Mod Configuration Menu entries");
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
