// Processor affinity.
//
// Far Cry 2 is a 2008 engine that behaves badly on machines far larger than anything it was tested
// on - the widely reported symptoms are physics and timing artefacts such as NPCs visibly bouncing.
// Restricting the process to fewer processors is the community's standard mitigation, and Far Cry 2
// Multi Fixer exposes it as a raw hexadecimal affinity mask. This offers the same thing as four
// choices, because "which bits do I want set" is not a question a player should have to answer.
//
// This is a workaround, not a fix: it makes the symptom rarer by giving the engine a smaller
// machine, and costs performance in exchange. It is a setting rather than something applied
// unconditionally precisely because it has a cost, and because the right answer depends on hardware
// this code cannot see.
//
// Nothing here touches the game. It is the one feature in UFCP that needs no engine knowledge at
// all - SetProcessAffinityMask is a plain Win32 call, so it works on any build, including ones
// nothing else here can resolve an address in.
//
// Note the 32-bit ceiling: an affinity mask in a 32-bit process is 32 bits wide, so on a machine
// with more than 32 logical processors Windows presents the process with one processor group and
// everything below operates within it. That is a Windows constraint, not a choice made here.
#include "fcse_api.h"

#include <windows.h>

#include <cstdio>

namespace {
    // Indices into the choice labels declared in main.cpp. The order is the file format - renaming
    // a label is free, reordering them silently changes what a saved fcse.ini means.
    enum AffinityMode : uint32_t {
        kAllCores = 0,
        kPhysicalCoresOnly = 1,
        kFourCores = 2,
        kOneCore = 3,
    };

    // One logical processor per physical core - the first of each core's siblings. Returns 0 if the
    // topology cannot be read, which callers treat as "no opinion" rather than as an empty mask,
    // since an empty mask would be an illegal argument to SetProcessAffinityMask.
    DWORD_PTR PhysicalCoreMask() {
        // 128 entries covers any machine a 32-bit process can see several times over; the call
        // reports what it needs and fails cleanly if this were ever too small.
        SYSTEM_LOGICAL_PROCESSOR_INFORMATION entries[128];
        DWORD length = sizeof(entries);
        if (!GetLogicalProcessorInformation(entries, &length)) {
            return 0;
        }

        DWORD_PTR mask = 0;
        const DWORD count = length / sizeof(entries[0]);
        for (DWORD i = 0; i < count; ++i) {
            if (entries[i].Relationship != RelationProcessorCore) {
                continue;
            }
            // Lowest set bit of the core's sibling mask. On a core without SMT that is the core
            // itself; on one with SMT it drops the hyperthread.
            const DWORD_PTR siblings = entries[i].ProcessorMask;
            mask |= siblings & (~siblings + 1);
        }
        return mask;
    }

    // The `count` lowest set bits of `mask`, or the whole mask if it has fewer.
    DWORD_PTR LowestBits(DWORD_PTR mask, unsigned count) {
        DWORD_PTR result = 0;
        for (unsigned taken = 0; taken < count && mask != 0; ++taken) {
            const DWORD_PTR lowest = mask & (~mask + 1);
            result |= lowest;
            mask &= ~lowest;
        }
        return result;
    }
}

void __cdecl OnAffinityChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    DWORD_PTR processMask = 0;
    DWORD_PTR systemMask = 0;
    if (!GetProcessAffinityMask(GetCurrentProcess(), &processMask, &systemMask)) {
        api->Log("affinity: the process affinity mask could not be read - left untouched");
        return;
    }

    // Everything is derived from the system mask rather than the process mask, so that narrowing
    // the selection and then widening it again in the same session restores every processor rather
    // than compounding down to one.
    const DWORD_PTR physical = PhysicalCoreMask() & systemMask;
    const DWORD_PTR countFrom = physical != 0 ? physical : systemMask;

    DWORD_PTR wanted = systemMask;
    switch (value->asChoice) {
    case kPhysicalCoresOnly:
        wanted = physical;
        break;
    case kFourCores:
        // Counted in physical cores where the topology is known, so "4 cores" means four separate
        // cores rather than two cores and their hyperthreads.
        wanted = LowestBits(countFrom, 4);
        break;
    case kOneCore:
        wanted = LowestBits(countFrom, 1);
        break;
    case kAllCores:
    default:
        break;
    }

    if (wanted == 0) {
        // Either the topology query failed for the physical-only choice, or the system mask itself
        // was empty. Refuse rather than pass an illegal mask that would leave the process pinned to
        // nothing.
        api->Log("affinity: the requested set of processors came out empty - left untouched");
        return;
    }

    if (!SetProcessAffinityMask(GetCurrentProcess(), wanted)) {
        char line[128];
        std::snprintf(line, sizeof(line), "affinity: could not be set to 0x%08zX, error %lu",
                      static_cast<size_t>(wanted), GetLastError());
        api->Log(line);
        return;
    }

    char line[128];
    std::snprintf(line, sizeof(line), "affinity: running on mask 0x%08zX",
                  static_cast<size_t>(wanted));
    api->Log(line);
}
