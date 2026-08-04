#pragma once

// Hooks Dunia.dll's real "build Options' row of category buttons" function (0x1081aee0 - found by
// disassembly: reloads its own `this` before each of 5 AddButton calls, structurally identical to
// BuildMainMenu's proven-safe use of the same pattern, and invoked only via a data/vtable xref, i.e.
// genuine lazy virtual dispatch when Options is actually shown) so FCSE can append its single
// Mod Configuration Menu navigation row after the real category buttons are built.
//
// See the plan file for the full RE trail, including an earlier wrong hook target (0x1084fa90 -
// fires eagerly before intro videos, is NOT this function) and why calling AddButton with
// CFCXOptionPage's own pointer (rather than this function's `this`) crashes the game.
namespace FCSE {

class ModsTab {
public:
    // Installs the hook via HookManager::Hook. Call once, after Dunia.dll is resolved and
    // HookManager::Initialize() has run, any time before RunGame() (the hooked function only
    // actually runs later, whenever the player first opens the Options screen). Logs and returns
    // false if the hook can't be installed; the game still runs normally either way, just without a
    // "Mods" tab.
    static bool Install();
};

} // namespace FCSE
