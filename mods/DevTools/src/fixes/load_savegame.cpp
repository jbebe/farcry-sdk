// -load: launching straight into a savegame crashes to desktop.
//
// The symptom: `FarCry2.exe -load <name>.sav` dies in a fraction of a second, with no window, no
// message and exit code 0, whatever save it is given.
//
// CFCXGameCmdLineParser::Process (Steam 0x10663D40) sends -load to 0x10661F50, which opens the save
// and parses it on a GameFileBlockingLoadThread. That succeeds. The thread then runs a validation
// pass through its context vtable slot +0x14 (Steam 0x1072EE60), resolving the save's records
// against engine registries by name, and faults on registries that do not exist yet:
//
//     1072ee8a  call 104DBB80h   ; ecx = [11644D74h], the settings manager - null
//     1072eeb3  call 10172820h   ; walks a registry at this+50h            - null
//
// 0x11644D74 is built by CCryEngine::Initialize, which InitDuniaEngine calls at +0x10CF, while the
// command line is dispatched at +0x52C. The pass discards every lookup result and its only durable
// effect is a flag on that registry, so before the registry exists it can accomplish nothing. The
// hook skips it and returns 1, the pass's own "resolved cleanly" result (5 is its failure code).
#include "fcse_api.h"

#include <cstdint>

namespace {
    // Spelled __fastcall so the detour receives the __thiscall `this` in ECX; the engine's `ret`
    // takes no stack arguments.
    using PostLoadResolveFn = uint32_t(__fastcall*)(void* self);

    // A pattern because the pass is only ever reached through a vtable slot, so the address library
    // has no entry to translate. It has to run to the end to be unique: the copy at Steam
    // 0x107FBA10 is byte-identical up to the record stride, walking 4-byte entries where this one
    // walks 8.
    FCSE::Relocation<uint8_t*> g_postLoadResolve{
        FCSE::Pattern("83 3D ?? ?? ?? ?? 00 53 55 56 57 8B 3D ?? ?? ?? ?? 8B F1 BD 01 00 00 00 75 "
                      "07 33 C9 E8 ?? ?? ?? ?? 6A 01 68 ?? ?? ?? ?? 8B CF E8 ?? ?? ?? ?? 8B 76 "
                      "74 8B 7E 08 83 C6 08")};

    PostLoadResolveFn g_original = nullptr;
    void** g_settingsManager = nullptr;

    uint32_t __fastcall PostLoadResolve(void* self) {
        if (*g_settingsManager == nullptr) {
            return 1;
        }
        return g_original(self);
    }
}

void ApplyLoadSavegameFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_postLoadResolve) {
        api->Log("-load: the post-load validation pass was not found in this build - not fixed");
        return;
    }

    uint8_t* site = g_postLoadResolve.get();

    // Operand of the matched `mov edi, [abs32]`: the settings manager the pass reads first, and the
    // cheapest proof that the engine is up. Read out of the match so it cannot disagree with the
    // site it guards.
    g_settingsManager = *reinterpret_cast<void***>(site + 13);

    if (api->Hook(site, reinterpret_cast<void*>(&PostLoadResolve),
                  reinterpret_cast<void**>(&g_original))) {
        api->Log("-load: savegame launches skip the post-load pass until the engine exists");
    }
}
