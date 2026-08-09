#pragma once

// Tier 2 of the plugin API: function detouring, backed by MinHook (vendored via CMakeLists.txt's
// FetchContent, same as tools/misc/modpatcher). Backs FCSE_PluginAPI::Hook.
//
// FCSE installs exactly one MinHook detour per target address - if two plugins ask to hook the
// same address, the second call is rejected and logged rather than chained (see the plan's
// "Overlap handling" section for why: composable hook-chaining needs call-order semantics and a
// shared dispatcher that aren't worth the complexity for a plugin ecosystem that doesn't exist
// yet). Ownership per address is tracked here, independent of - but consistent with - MinHook's
// own internal "already hooked" rejection.
namespace FCSE {

class HookManager {
public:
    // MH_Initialize() once, at loader startup, after Dunia.dll is loaded and before any plugin's
    // FCSE_Load runs.
    static bool Initialize();

    // MH_Uninitialize() once, at loader shutdown (after RunGame returns).
    static void Shutdown();

    // Backs FCSE_PluginAPI::Hook. Captures the calling plugin's identity itself via
    // _ReturnAddress(), so callers never pass an identifier. Returns false (logged) if `target`
    // is null, MinHook itself fails, or another plugin already owns a hook on this address.
    static bool Hook(void* target, void* detour, void** original);
};

} // namespace FCSE
