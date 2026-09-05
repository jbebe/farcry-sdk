// Developer console.
//
// Far Cry 2 ships a working console on `~`, but most of what it can do is hidden: commands
// registered as ConsoleDeveloperOnly are filtered out of the `?` listing and out of command lookup
// alike, so typing one answers "Unknown command". About 57 commands sit behind that flag - see
// docs/docs/engine-internals/developer-console.md.
//
// The flag they are tested against, CXConsole+0x68, is zero for anything the player types, and the
// only code that raises it lowers it again immediately - so there is no value to write and make
// stick. What there is instead is one predicate - is the flag clear and is this element
// developer-only? - that the compiler emitted six times over: once out of line, and inlined at five
// call sites. Each copy is the same branch, which skips the developer test when the flag is set
// (Steam Dunia.dll):
//
//     10296161  38 59 68   cmp byte ptr [ecx+68h], bl   ; CXConsole::ExecuteCommand
//     1029616D  75 09      jnz short +9                 ; flag set -> skip the test below
//     1029616F  38 58 40   cmp byte ptr [eax+40h], bl   ; element is ConsoleDeveloperOnly?
//     10296172  0F 85 ..   jnz  ...                     ; yes -> refuse the command
//
// Turning each `jnz` into `jmp` takes the developer test out of the path permanently. That is one
// byte per site - 75 -> EB, JNZ rel8 to JMP rel8 - and because the displacement is untouched the
// jump still lands exactly where it did.
//
// Four of the six copies are patched: the console's own lookup, execute and two listing loops. The
// remaining two sit behind element lookups the engine makes for itself - InitCVars,
// SetupOnlineEngineRegistery, per-frame Update - which are not on the path from a typed line to a
// command, so widening them would change engine behaviour for no gain here.
//
// This deliberately leaves the *other* gate alone. Every copy tests a context mask (CXConsole+0x64
// against element+0x3c) straight afterwards, which is what keeps multiplayer-only and editor-only
// commands out of a single-player console; only the developer test is lifted.
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr uint8_t kJnz = 0x75;
    constexpr uint8_t kJmp = 0xEB;

    // A `jnz` that skips the developer test, and where that opcode sits inside the match. The
    // patterns start ahead of the branch because the two listing loops are byte-identical from it
    // onwards, and only the instruction before them tells the two apart.
    struct Gate {
        FCSE::Relocation<uint8_t*> site;
        size_t opcodeOffset;
    };

    Gate g_gates[] = {
        // The out-of-line copy of the predicate, which CXConsole::ExecuteString calls to decide
        // whether the name it just looked up counts as found. This is the one that answers
        // "Unknown command" - without it the rest only make hidden commands visible, not runnable.
        {FCSE::Relocation<uint8_t*>{
             FCSE::Pattern("80 79 68 00 8B 54 24 04 75 0B 80 7A 40 00 74 05 32 C0 C2 04 00")},
         8},
        // CXConsole::ExecuteCommand - a second check once the command has been found.
        {FCSE::Relocation<uint8_t*>{
             FCSE::Pattern("38 59 68 56 57 89 8C 24 ?? ?? ?? ?? 75 09 38 58 40")},
         12},
        // The `?` listing walks two collections and applies the same test to each.
        {FCSE::Relocation<uint8_t*>{FCSE::Pattern("80 7D 68 00 8B 7E 28 75 06 80 7F 40 00")}, 7},
        {FCSE::Relocation<uint8_t*>{FCSE::Pattern("80 7D 68 00 8D 7E 28 75 06 80 7F 40 00")}, 7},
    };

    // Writes `opcode` over every gate, and reports whether all of them took it.
    bool WriteGates(uint8_t opcode) {
        const FCSE_PluginAPI* api = FCSE::ApiPointer();
        bool all = true;

        for (auto& gate : g_gates) {
            if (!gate.site || !api->Patch(gate.site.get() + gate.opcodeOffset, &opcode, 1)) {
                all = false;
            }
        }
        return all;
    }
}

void __cdecl OnDeveloperConsoleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const bool enable = value->asCheckbox;
    const bool everyGate = WriteGates(enable ? kJmp : kJnz);

    // Disabling with a gate unresolved needs no warning: one that never resolved was never patched.
    const char* line = "developer console: off - the console is as it shipped";
    if (enable) {
        line = everyGate
                   ? "developer console: on - developer-only commands are listed by '?' and will run"
                   : "developer console: on, but not every developer test was found in this build - "
                     "some hidden commands stay hidden";
    }
    api->Log(line);
}
