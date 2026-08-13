#include "api/plugin_api.h"

#include "api/function_registry.h"
#include "api/hook.h"
#include "api/patch.h"
#include "api/pattern_scan.h"
#include "api/settings_registry.h"
#include "engine/address_library.h"
#include "engine/dunia_api.h"
#include "log.h"

#include <intrin.h>

namespace FCSE {

namespace {

    // Each shim exists for one reason: to capture _ReturnAddress() at the boundary, so FCSE can
    // name the plugin that called without the plugin having to identify itself - and without being
    // able to get it wrong.

    void __cdecl PluginLogShim(const char* message) {
        Log::FromCaller(_ReturnAddress(), message != nullptr ? message : "");
    }

    void __cdecl PluginAddFunctionCBShim(void* fn, const char* name) {
        FunctionRegistry::Register(fn, name);
    }

    uintptr_t __cdecl PluginFindPatternShim(const char* pattern, uint32_t* outMatchCount) {
        return PatternScan::Find(pattern, outMatchCount, _ReturnAddress());
    }

    FCSE_GameBuild ToPluginBuild(DuniaBuild build) {
        switch (build) {
            case DuniaBuild::Retail103: return FCSE_GAME_BUILD_103_RETAIL;
            case DuniaBuild::Uplay103:  return FCSE_GAME_BUILD_103_UPLAY;
            default:                    return FCSE_GAME_BUILD_UNKNOWN;
        }
    }

    DuniaBuild FromPluginBuild(FCSE_GameBuild build) {
        switch (build) {
            case FCSE_GAME_BUILD_103_RETAIL: return DuniaBuild::Retail103;
            case FCSE_GAME_BUILD_103_UPLAY:  return DuniaBuild::Uplay103;
            default:                         return DuniaBuild::Unknown;
        }
    }

    uintptr_t __cdecl PluginResolveFromShim(FCSE_GameBuild sourceBuild, uint32_t rva) {
        // Accept a VA at Dunia's preferred base as well as an RVA. Far Cry 2 addresses are quoted
        // both ways in about equal measure - Ghidra shows 0x1081E9C0, the docs and this codebase
        // mostly use 0x0081E9C0 - and a plugin author should not have to know which this wanted.
        // No real RVA reaches the preferred base, so the two forms cannot be confused.
        constexpr uint32_t kPreferredBase = 0x10000000u;
        if (rva >= kPreferredBase) {
            rva -= kPreferredBase;
        }
        return AddressLibrary::AddressFrom(FromPluginBuild(sourceBuild), rva);
    }

}

const FCSE_PluginAPI* PluginApi::Build(const BuildInfo& build) {
    static FCSE_PluginAPI api{};

    api.apiVersion = FCSE_API_VERSION;
    api.duniaModule = DuniaApi::Module();
    api.duniaBase = DuniaApi::Base();
    api.duniaSize = DuniaApi::Size();
    // The address library, handed to plugins so they can be build-agnostic the same way FCSE now
    // is. Without these a plugin has no choice but to bake an RVA and work on one build only.
    api.ResolveFrom = &PluginResolveFromShim;
    api.FindPattern = &PluginFindPatternShim;
    api.gameBuild = ToPluginBuild(build.build);
    api.gameBuildId = build.id;
    api.addressMapping = AddressLibrary::MappingVersion().c_str();
    api.Log = &PluginLogShim;
    api.AddFunctionCB = &PluginAddFunctionCBShim;
    api.Hook = &HookManager::Hook;
    api.Patch = &PatchManager::Patch;
    api.RegisterSettings = &SettingsRegistry::RegisterSettings;

    return &api;
}

}
