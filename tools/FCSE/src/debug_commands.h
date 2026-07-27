#pragma once

#include <string>

// Reimplementation of FarCry2.exe's own RegisterDebugCommands and its 12 handlers (see
// docs/docs/engine-internals/launcher-exe.md's table) - FCSE.exe must reproduce these exactly,
// because docs/docs/engine-internals/function-registry.md confirms several of them are live
// gameplay hooks (diamond pickups, malaria progression, main-menu construction, loading-screen
// text), not just QA stubs. Dropping any of them would be a real behavioral regression against
// the stock exe, not a cosmetic one.
namespace FCSE {

class DebugCommands {
public:
    // Reads MalariaCurve/PlayerSPFinalize's real constants from the real FarCry2.exe (see
    // stock_constants.h). Call once, before Dunia.dll can invoke Provider() below.
    static void Init(const std::wstring& directory);

    // The provider callback handed to Dunia.dll via RegisterGameFunctionProvider, matching what
    // FarCry2.exe's own WinMain passes. Dunia.dll invokes this exactly once, after
    // InitDuniaEngine succeeds - the only point at which the function registry is guaranteed to
    // exist. Runs every loaded plugin's optional FCSE_OnRegisterFunctions first, then registers
    // this reimplementation's 12 stock handlers - in that order, so a plugin can override a stock
    // name (Dunia's FunctionRegistry_Insert is first-claimant-wins, confirmed via decompile).
    static void __cdecl Provider();
};

} // namespace FCSE
