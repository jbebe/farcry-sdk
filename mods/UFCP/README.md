# UFCP — Unofficial Far Cry Patch

Bugs Ubisoft never patched, fixed in the running game instead of in the files on disk.

UFCP is an [FCSE](../../tools/FCSE) plugin: it patches `Dunia.dll` in memory at startup, so it
overwrites nothing, leaves no trace when uninstalled, and stacks with whatever data mods are already
installed.

## Fixes

Applied unconditionally, with no setting — a fix that needs a switch is a preference in disguise.

| Fix | What it does |
|---|---|
| Jackal tapes | The same Jackal tape recording — usually *#09. Stealing Boots* — plays every time in the southern map instead of advancing through the set. |
| Predecessor tapes | Restores the seven Intel Bonus predecessor missions, which ship in the game files but are held behind an ownership check no longer able to succeed. |
| Machetes | Restores the Primitive and Homemade machete variants, held behind the same kind of check. Pick one in the game's own Options → Game → Machete Type. |

The two restorations are not a bypass of anything anyone can still buy. Both were Ubisoft promotions
that ended; the Steam build asks a retired Uplay privileges service and the GOG build reads a
registry key that was written by redeeming a code. Neither has a correct answer left to give, so the
content is unreachable in every copy of the game.

## Options

Preferences, where the right answer depends on the player or their hardware. One row each in the Mod
Configuration Menu, saved under `[UFCP]` in `bin\fcse.ini`. **Both default to leaving the game
exactly as it shipped**, so installing UFCP applies its fixes and changes nothing else.

| Option | Range | Default |
|---|---|---|
| Field of view | 65–120 degrees | **75** — the game's own value, at which the feature disables itself entirely |
| Processor affinity | All cores · Physical cores only · 4 cores · 1 core | **All cores** |

**Field of view** substitutes the argument to `CCameraComponent`'s `fFOV` property setter. That
property is set when a camera entity is created, so a change reaches the world on the next load
rather than instantly, and it applies to every camera that sets `fFOV` — there is no separate
"player camera" property to target. That is why 75 means "leave the engine alone" rather than "force
75 everywhere". Past about 120 the first-person weapon models distort and the near plane clips,
which is where the ceiling comes from.

**Processor affinity** is a workaround, not a fix: the engine misbehaves on machines far larger than
anything it was tested on (the reported symptom is NPCs visibly bouncing), and giving it a smaller
machine makes that rarer at the cost of performance. *Physical cores only* drops SMT siblings; *4
cores* and *1 core* count in physical cores where the topology can be read. It is the one feature
here that needs no engine knowledge at all, so it works on any build.

## Jackal tapes

The tape picker walks an array of tape records looking for the first eligible one, testing two
flags: whether that tape has already been played, and whether it belongs to the region the player is
in. The branch that handles "already played" jumps into a tail shared with the not-played case, and
that tail tests only the region flag — so in the half of the map where the region check fails, a
played tape is treated as eligible. The picker returns the first match, so once a tape is marked
played it stays the first match forever, and every pickup replays it.

The fix retargets that one jump at the head of the loop to the loop's own "next record" label, so a
played tape is skipped. Two bytes.

The community investigated this in 2011 and again in 2016 without finding a cause
([gotchas](../../docs/docs/modding/gotchas.md)); FoxAhead's
[Far Cry 2 Multi Fixer](https://github.com/FoxAhead/Far-Cry-2-Multi-Fixer) shipped the same one-byte
edit without describing what it does. The annotated disassembly and the derivation are in
[`src/fixes/jackal_tapes.cpp`](src/fixes/jackal_tapes.cpp).

## How a feature finds the code it patches

Where the address library can name a site, it is used: `IsMachetesUnlocked()` is
`FCSE::Uplay(0x000488D0)`, and FCSE translates that to the running build.

Most sites it cannot name. The library is keyed by exact entries — function starts and data
addresses — and almost every site worth patching is *inside* a function, one branch displacement
halfway down a loop. The two shipped builds are not a constant distance apart either, so no
arithmetic gets there. Those sites use `FCSE::Relocation{FCSE::Pattern(...)}`, matched on the bytes
about to be replaced. That is one mechanism doing three jobs: it finds the code, it verifies the
code is what the feature was written against, and it works on any build whose code still looks the
same rather than only the two that are mapped. FCSE reports a pattern that matched in more than one
place as no match at all, so a feature either lands on its one site or logs that it could not.

Sometimes the builds diverge outright — the predecessor-tapes gate is a privileges call on Steam and
a registry read on GOG, with no counterpart in the other build at all. That is two patterns, one per
build, and whichever resolves is the one that is there.

Every pattern is checked against both shipped `Dunia.dll` builds, parsed straight out of these
sources, by `verify_patterns.py`: exactly one match on at least one build, never two on any.

## Building

```
.\build.ps1
.\build.ps1 -Config debug
.\build.ps1 -Install "C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin"
```

Needs the same x86 MSVC toolchain as FCSE, and nothing else — the whole dependency is
`tools/FCSE/include/fcse_api.h`, the one header a third-party plugin would copy out of the tree.

## Installing

1. Install FCSE (`FCSE.exe` next to the game's `FarCry2.exe`).
2. Drop `UFCP.dll` into `bin\plugins\`.
3. Launch `FCSE.exe`.

## Verification

- `bin\fcse.log` shows `UFCP loaded`, then `jackal tapes fixed`, `predecessor tapes unlocked`,
  `machetes unlocked`, the FOV hook's address, and the affinity mask.
- `bin\fcse.ini` gains a `[UFCP]` group with `Field of view = 75` and
  `Processor affinity = All cores`.
- Moving the FOV slider logs the new value; setting it back to 75 logs that the game's own value is
  no longer being overridden.
- **In-game, none of which has been run yet**: tapes in the southern map advance instead of
  repeating; the predecessor-mission envelope appears in the central town; Options → Game offers
  Machete Type with three entries; a changed FOV takes effect after a load.
