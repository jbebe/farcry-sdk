#include "debug_commands.h"

#include "function_registry.h"
#include "log.h"
#include "lua/lua_host.h"
#include "plugin_loader.h"
#include "stock_constants.h"

#include <cstdint>

namespace FCSE {

namespace {
    StockConstants g_constants;

    // FunctionRegistry_Invoke always calls the stored handler with exactly 2 raw args (see
    // docs/docs/engine-internals/function-registry.md's decompiled dispatch): every handler below
    // uses this same 2-parameter shape regardless of how many it actually reads, matching the
    // original exe's own handlers exactly.

    int __cdecl ToRed(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = 1;
        return 0;
    }

    int __cdecl MenuJoke(void* param1, void* /*param2*/) {
        return *reinterpret_cast<int*>(param1);
    }

    int __cdecl LoadGame_Stub(void* /*param1*/, void* /*param2*/) {
        return 1;
    }

    int __cdecl SelectStoryMission(void* param1, void* /*param2*/) {
        return *reinterpret_cast<int*>(param1) + 10;
    }

    int __cdecl SelectLibraryMission(void* param1, void* /*param2*/) {
        return *reinterpret_cast<int*>(param1) + 0x15;
    }

    int __cdecl MalariaCurve(void* param1, void* /*param2*/) {
        *reinterpret_cast<float*>(param1) *= g_constants.malariaCurveMultiplier;
        return 0;
    }

    int __cdecl AddDiamond(void* param1, void* param2) {
        *reinterpret_cast<int*>(param1) += *reinterpret_cast<int*>(param2);
        return 0;
    }

    int __cdecl SetDefaultTimeOut(void* param1, void* param2) {
        *reinterpret_cast<int*>(param1) = *reinterpret_cast<int*>(param2);
        return 0;
    }

    int __cdecl SetLoadingText(void* param1, void* /*param2*/) {
        *reinterpret_cast<int16_t*>(param1) = 0;
        return 0;
    }

    int __cdecl PlayerSPFinalize(void* param1, void* /*param2*/) {
        *reinterpret_cast<int*>(param1) = g_constants.playerSPFinalizeValue;
        return 0;
    }

    int __cdecl InitializeUseableEvent_Stub(void* param1, void* /*param2*/) {
        *reinterpret_cast<uint8_t*>(param1) = 1;
        return 0;
    }

    int __cdecl SaveGame_Stub(void* /*param1*/, void* /*param2*/) {
        return 0;
    }
}

void DebugCommands::Init(const std::wstring& directory) {
    g_constants = LoadStockConstants(directory);
}

void __cdecl DebugCommands::Provider() {
    Log::Loader("provider callback invoked by Dunia.dll - running plugin registrations, then "
                "stock handlers");

    // Plugins first: FunctionRegistry_Insert is first-claimant-wins, so this is what lets a
    // plugin override one of the 12 stock names below (e.g. its own AddDiamond). Scripts come
    // after the compiled plugins for the same reason, and both come before the stock handlers.
    PluginLoader::RunOnRegisterFunctions();
    LuaHost::OnRegisterFunctions();

    FunctionRegistry::Register(reinterpret_cast<void*>(&ToRed), "toRed");
    FunctionRegistry::Register(reinterpret_cast<void*>(&MenuJoke), "menuJoke");
    FunctionRegistry::Register(reinterpret_cast<void*>(&LoadGame_Stub), "mapJoke");
    FunctionRegistry::Register(reinterpret_cast<void*>(&LoadGame_Stub), "LoadGame");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SelectStoryMission), "SelectStoryMission");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SelectLibraryMission),
                                "SelectLibraryMission");
    FunctionRegistry::Register(reinterpret_cast<void*>(&MalariaCurve), "MalariaCurve");
    FunctionRegistry::Register(reinterpret_cast<void*>(&AddDiamond), "AddDiamond");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SetDefaultTimeOut), "SetDefaultTimeOut");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SetLoadingText), "SetLoadingText");
    FunctionRegistry::Register(reinterpret_cast<void*>(&PlayerSPFinalize), "PlayerSPFinalize");
    FunctionRegistry::Register(reinterpret_cast<void*>(&InitializeUseableEvent_Stub),
                                "InitializeUseableEvent");
    FunctionRegistry::Register(reinterpret_cast<void*>(&InitializeUseableEvent_Stub),
                                "CheckDomino");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SaveGame_Stub), "incHB");
    FunctionRegistry::Register(reinterpret_cast<void*>(&SaveGame_Stub), "SaveGame");
}

} // namespace FCSE
