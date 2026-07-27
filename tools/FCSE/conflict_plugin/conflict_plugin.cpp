// conflict_plugin - deliberately targets the same tier-1 name and tier-2 hook address as
// example_plugin, so loading both together (in either order) exercises FCSE's conflict-rejection
// path end to end. Not a real mod - a smoke test fixture for tools/FCSE (see its README's
// verification section) and a template for what a rejected plugin's own log output looks like.
#include "../include/plugin_api.h"

#include <windows.h>

namespace {
    using GetTickCountFn = DWORD(WINAPI*)();
    GetTickCountFn g_originalGetTickCount = nullptr;

    DWORD WINAPI GetTickCountDetour() {
        return g_originalGetTickCount();
    }

    // Same name as example_plugin.cpp's override, different effect (writes 2 instead of 0) purely
    // so the two are distinguishable if this one ever wins instead of example_plugin's - whichever
    // plugin loads first claims "toRed"; the other's registration is rejected and logged.
    int __cdecl ToRedOverrideConflicting(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = 2;
        return 0;
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false;
    }

    api->Log("conflict_plugin loaded");

    // Same target as example_plugin's Hook() call - whichever plugin loads first wins, the other
    // gets false back and a logged conflict naming both plugins.
    void* target = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "GetTickCount");
    bool hooked = api->Hook(target, reinterpret_cast<void*>(&GetTickCountDetour),
                             reinterpret_cast<void**>(&g_originalGetTickCount));
    api->Log(hooked ? "conflict_plugin: GetTickCount hook installed (won the race this run)"
                    : "conflict_plugin: GetTickCount hook rejected, see fcse.log for who owns it");

    return true;
}

extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api) {
    api->AddFunctionCB(reinterpret_cast<void*>(&ToRedOverrideConflicting), "toRed");
    api->Log("conflict_plugin: toRed registration attempted, see fcse.log for whether it won");
}
