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
| Savegame launch | `-load <name>.sav` quit to desktop in a fraction of a second instead of booting straight into the save. The command line's own way to skip the menus, unusable as shipped. |
| Exit crash | Quitting through Exit Game faulted instead of closing cleanly — the reason "Far Cry 2 crashes on exit" is folklore, and a guaranteed false positive on top of any real crash you are trying to read. |

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

## Savegame launch

`-load` opens the save and parses it correctly, then dies in what it does next: a validation pass
that resolves the save's records against engine registries by name. The command line is dispatched
from `InitDuniaEngine+0x52C` and those registries are not built until `CCryEngine::Initialize` at
`+0x10CF`, so the pass always runs against an engine that does not exist yet and faults on the first
registry it reads.

The fix skips the pass while the engine is absent, returning the pass's own "resolved cleanly"
result; once the engine is up it runs untouched. Skipping is safe rather than merely expedient: the
pass discards every lookup result, and its only durable effect is a flag on a registry that has not
been constructed, so before the engine exists there is nothing for it to accomplish.

The save name needs its extension — `-load <name>.sav`, not `-load <name>`. That part is not a bug:
the parser takes the basename verbatim and appends nothing, so a name without `.sav` genuinely
matches no file.

## Exit crash

A registry teardown hands each of its (owner, object) pairs to a function that walks the object and
then destroys it, but some of those objects have already been destroyed through another path. The
engine null-checks the pointer, which does not help: the pointer is non-null and the object behind
it is gone. Three different faults come out of that one bug depending on how the dead allocation has
decayed — an unmapped page, a zeroed vtable, a null destructor slot.

The fix tests the object with `VirtualQuery` and skips the teardown when it is already dead.
Catching the fault instead would also shut the game down cleanly, but the access violation still
happens, and a first-chance exception is what a crash handler reports — trading a crash for a crash
log that cries wolf on every exit is not a fix.

The teardown is not exit-only: the same function is reached from magma's list-widget
remove-all-items path, so the guard also runs on every UI list repopulation. That is menu-event
rate rather than frame rate, which is what makes a `VirtualQuery` per object affordable. The first
object it skips is logged once, with the vtable RVA where one is still readable — the class identity
of whatever is being destroyed twice is the one thing a real fix would need, and this is the only
place it surfaces.

This is the game's bug and not FCSE's, which was worth ruling out rather than assuming: FCSE loads
its own `fcse.mgb` into the same magma engine and never unloads it, so the stale entry could have
been its. Two indirect attempts failed — the package loads when the menu is built rather than when
Options is opened, so no ordinary FCSE run avoids it, and an unmodded `FarCry2.exe` shows nothing
because the engine swallows the access violation itself and exits 0, leaving no Windows error
record. Disabling `MagmaPackage::Load()` in a throwaway FCSE build settled it in one run: with the
package never loaded, the guard still skipped a destroyed object.

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
`tools/FCSE/include/fcse_api.h`, the one header a third-party plugin would copy out of the tree. No
.NET SDK, unlike FCSE: UFCP embeds no `.mgb` layouts, so JackAll is not in its build.

`.\verify_build.ps1 [-Config debug]` checks the three properties of a built `UFCP.dll` that fail
*silently* — x86, static CRT, and the `FCSE_Load` export. All three produce a plugin that is simply
never there, with FCSE itself starting up perfectly. Both it and the build run in both
configurations on every push and pull request touching `mods/UFCP` or the plugin ABI header
(`.github/workflows/ufcp-ci.yml`), and again before a release is packaged
(`.github/workflows/ufcp-release.yml`, dispatched with a version, producing `ufcp-{version}.zip`).

There is no test suite, deliberately. UFCP is byte patches and hooks against a live `Dunia.dll`; it
holds no pure logic worth a suite, and stubbing the engine to manufacture some would test the stub.

## Installing

1. Install FCSE (`FCSE.exe` next to the game's `FarCry2.exe`).
2. Drop `UFCP.dll` into `bin\plugins\`.
3. Launch `FCSE.exe`.

## Verification

*(automated — CI)* The build and `verify_build.ps1` in both configurations.

*(automated — local, needs the game)* `python verify_patterns.py` re-checks every byte pattern
against both shipped `Dunia.dll` builds. It cannot run in CI, because that would mean putting a copy
of the game in the repository. Run it after touching a pattern.

Everything below needs a real install:

- `bin\fcse.log` shows `UFCP loaded`, then `jackal tapes fixed`, `predecessor tapes unlocked`,
  `machetes unlocked`, both guards installing, the FOV hook's address, and the affinity mask.
- **Savegame launch** *(run, works)*: `FCSE.exe -load <name>.sav` boots straight into that save.
- **Exit crash** *(run, works)*: quitting through Exit Game ends with `RunGame returned true` and no
  `CRASH:` line — verified both from the menu and after a `-load` launch.
- `bin\fcse.ini` gains a `[UFCP]` group with `Field of view = 75` and
  `Processor affinity = All cores`.
- Moving the FOV slider logs the new value; setting it back to 75 logs that the game's own value is
  no longer being overridden.
- **In-game, none of which has been run yet**: tapes in the southern map advance instead of
  repeating; the predecessor-mission envelope appears in the central town; Options → Game offers
  Machete Type with three entries; a changed FOV takes effect after a load.
