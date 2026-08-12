// Jackal tapes: the same recording plays every time in the southern map.
//
// The symptom, as players report it: after the first Jackal tape you pick up in the southern half of
// the map, every subsequent tape there plays the same recording - usually "#09. Stealing Boots" -
// instead of advancing through the set. Investigated by the modding community in 2011 and again in
// 2016 without a root cause; FoxAhead's Far Cry 2 Multi Fixer shipped the one-byte fix below without
// publishing what it does. This is what it does.
//
// The tape picker (Steam Dunia.dll 0x1074E3F0, GOG 0x10740EE0) walks an array of 0x78-byte records -
// base at this+0x158, count at this+0x15C - and stops at the first record it considers eligible:
//
//     +0x74  byte   already played
//     +0x75  byte   belongs to the region identified by the constant below
//
// It compares the current region id (ECX) against a constant (EDX, 0x6C33888C), so the two halves of
// the map take different branches. Annotated at Steam addresses:
//
//     1074E460  80 7E 74 00   cmp  byte [esi+74h], 0   ; loop head: already played?
//     1074E464  75 0A         jne  1074E470            ;   yes -> skip the not-played tests
//     1074E466  3B CA         cmp  ecx, edx            ; --- not played ---
//     1074E468  75 0A         jne  1074E474
//     1074E46A  80 7E 75 00   cmp  byte [esi+75h], 0   ; region matches: take it if it is ours
//     1074E46E  75 16         jne  1074E486            ;   -> select
//     1074E470  3B CA         cmp  ecx, edx            ; --- shared tail ---
//     1074E472  74 06         je   1074E47A            ;   region matches -> next record
//     1074E474  80 7E 75 00   cmp  byte [esi+75h], 0   ; region differs: take it if it is not ours
//     1074E478  74 0C         je   1074E486            ;   -> select
//     1074E47A  83 C6 78      add  esi, 78h            ; next record
//     1074E47D  3B F0         cmp  esi, eax
//     1074E47F  75 DF         jne  1074E460
//     1074E486  88 5E 74      mov  [esi+74h], bl       ; selected: mark played (bl = 1), then play
//
// The bug is the target of the `jne` at 1074E464. A record that has already been played jumps into
// the *shared tail*, and that tail's only test is the region flag. So when the region id does not
// match the constant - the southern map - an already-played record with +0x75 == 0 is selected
// exactly as readily as an unplayed one. The picker returns the first record it finds, so once any
// such record is marked played it is still the first one found on the next pickup, and on every
// pickup after that. The "already played" test is not weakened on that path; it is skipped entirely.
//
// The fix retargets that one jump to the loop's own "next record" label, so a played record is
// always skipped: `jne +0Ah` becomes `jne +14h`.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The site is inside a function, which is why this is a pattern rather than an address: FCSE's
    // address library is keyed by function starts and data addresses, so ResolveFrom cannot name an
    // instruction halfway down a loop. The bytes below occur exactly once in the code section of
    // both shipped builds - measured, not assumed - and FCSE reports a pattern that matches more
    // than once as no match at all, so this either finds the one site or finds nothing.
    //
    // Matching on the original bytes also makes the pattern its own safety check: it cannot resolve
    // on a build whose code differs, and it cannot resolve twice, because the first match is cached
    // before the first write changes the bytes it matched on.
    FCSE::Relocation<uint8_t*> g_loopHead{FCSE::Pattern("75 0A 3B CA 75 0A 80 7E 75 00 75 16")};

    constexpr uint8_t kFixed[] = {0x75, 0x14}; // jne  +14h  -> next record
}

void ApplyJackalTapesFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_loopHead) {
        api->Log("jackal tapes: the tape picker's loop was not found in this build - not fixed");
        return;
    }

    // A failed Patch() is FCSE's to explain - it names the plugin that already owns these bytes.
    if (api->Patch(reinterpret_cast<void*>(g_loopHead.address()), kFixed, sizeof(kFixed))) {
        api->Log("jackal tapes fixed");
    }
}
