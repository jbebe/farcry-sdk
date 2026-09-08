#pragma once

#include "fcse_api.h"

namespace FCSE {

// Tier 2 of the plugin API: function detouring and mid-function hooks, backed by safetyhook.
class HookManager {
public:
    // Removes every hook, at loader shutdown (after RunGame returns).
    static void Shutdown();

    // Backs FCSE_PluginAPI::Hook. Captures the calling plugin's identity via _ReturnAddress().
    static bool Hook(void* target, void* detour, void** original);

    // Backs FCSE_PluginAPI::MidHook. Same identity capture and conflict rules as Hook.
    static bool MidHook(void* target, FCSE_MidHookHandler handler);
};

}
