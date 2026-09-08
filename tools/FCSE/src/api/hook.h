#pragma once

#include "fcse_api.h"

namespace FCSE {

// Tier 2 of the plugin API: function detouring and mid-function hooks, backed by safetyhook
// (vendored via CMakeLists.txt's FetchContent). Backs FCSE_PluginAPI::Hook and ::MidHook.
//
// One hook per site, first claimant wins: a second plugin asking for an address another plugin
// already hooks is rejected and logged rather than chained.
class HookManager {
public:
    // Removes every hook, at loader shutdown (after RunGame returns).
    static void Shutdown();

    // Backs FCSE_PluginAPI::Hook. Captures the calling plugin's identity itself via
    // _ReturnAddress(). Returns false (logged) if `target` is null, its first instructions cannot
    // be relocated, or another plugin already owns a hook within 5 bytes of it.
    static bool Hook(void* target, void* detour, void** original);

    // Backs FCSE_PluginAPI::MidHook. Same identity capture and rejection rules as Hook.
    static bool MidHook(void* target, FCSE_MidHookHandler handler);
};

}
