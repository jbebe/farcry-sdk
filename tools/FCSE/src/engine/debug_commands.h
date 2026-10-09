#pragma once

// Reimplementation of FarCry2.exe's own RegisterDebugCommands and its 12 handlers (see
// docs/docs/engine-internals/launcher-exe.md's table) - FCSE.exe must reproduce these exactly,
// because docs/docs/engine-internals/function-registry.md confirms several of them are live
// gameplay hooks (diamond pickups, malaria progression, main-menu construction, loading-screen
// text), not just QA stubs. Dropping any of them would be a real behavioral regression against
// the stock exe, not a cosmetic one.
namespace FCSE {

class DebugCommands {
public:
    // Claims the 12 stock names. Called from the provider callback FCSE hands Dunia.dll, and
    // deliberately last in it: FunctionRegistry_Insert is first-claimant-wins, so anything a
    // plugin or script registered first keeps the name.
    static void RegisterStockHandlers();
};

} // namespace FCSE
