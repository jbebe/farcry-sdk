# UFCP — Unofficial Far Cry Patch

Bugs Ubisoft never patched, fixed in the running game instead of in the files on disk.

UFCP is an [FCSE](../../tools/FCSE) plugin: it patches `Dunia.dll` in memory at startup, so it
overwrites nothing, leaves no trace when uninstalled, and stacks with whatever data mods are already
installed.

This is the plugin a player installs. The things only someone working on the game wants — the
developer console, booting straight into a save — are a separate plugin, [DevTools](../DevTools),
so a player is not carrying them and a modder can install either or both.

## Fixes

Applied unconditionally, with no setting — a fix that needs a switch is a preference in disguise.

| Fix | What it does |
|---|---|
| Jackal tapes | The same Jackal tape recording — usually *#09. Stealing Boots* — plays every time in the southern map instead of advancing through the set. |
| Predecessor tapes | Restores the seven Intel Bonus predecessor missions, which ship in the game files but are held behind an ownership check no longer able to succeed. |
| Machetes | Restores the Primitive and Homemade machete variants, held behind the same kind of check. Pick one in the game's own Options → Game → Machete Type. |
| Exit crash | Quitting through Exit Game faulted instead of closing cleanly — the reason "Far Cry 2 crashes on exit" is folklore, and a guaranteed false positive on top of any real crash you are trying to read. |
| Mouse speed cap | A fast flick turned the view less far than a slow one: the gamepad filter's output ceiling was applied to the mouse too, so quick movement saturated. |
| Controller vibration | No pad ever rumbled on PC. The curves are evaluated and the dispatcher calls XInput — the two motor amplitudes were simply pushed as zeroes. |
| High-precision timer | Loading screens ran far below the 30 FPS they are paced for, because the default 15.6 ms timer resolution overshoots every sleep. |
| CPU and GPU utilisation | The job pool was sized for two-core machines, idle workers spun in the queue's critical section, the GPU could never run ahead a frame, and with a frame cap set the limiter burned a whole core busy-waiting. |

The two restorations are not a bypass of anything anyone can still buy. Both were Ubisoft promotions
that ended; the Steam build asks a retired Uplay privileges service and the GOG build reads a
registry key that was written by redeeming a code. Neither has a correct answer left to give, so the
content is unreachable in every copy of the game.

## Options

Preferences, where the right answer depends on the player or their hardware. One row each in the Mod
Configuration Menu, saved under `[UFCP]` in `bin\fcse.ini`. **All but three default to leaving the
game exactly as it shipped.** The exceptions are skip intro, skip title screen and a 60 Hz frame
cap, which are on out of the box because they are what someone installing a patch is asking for —
each is one row away from the stock behaviour if you disagree.

| Option | Range | Default |
|---|---|---|
| Field of view | 65–120 degrees | **75** — the game's own |
| Viewmodel field of view | 45–140 degrees | **75** — the game's own |
| Ironsight field of view | 0–140 degrees | **0** — the weapon's own |
| Vehicle field of view | 0–140 degrees | **0** — the vehicle's own |
| Mouse look sensitivity | 0.10×–5.00× | **1.00×** |
| Controller look sensitivity | 0.05×–2.00× | **1.00×** |
| Controller aim assist | on · off | **on** — the game's own helpers |
| Aim toggle | on · off | **off** |
| Controller aim toggle | on · off | **off** |
| Sprint toggle | on · off | **off** |
| Full turn rate while sprinting | on · off | **off** |
| Skip intro videos | on · off | **on** |
| Skip title screen | on · off | **on** |
| Maximum frame rate | Game default · Unlocked · Screen refresh · 30–240 Hz | **60 Hz** |
| Display mode | Game default · Borderless | **Game default** |
| Processor affinity | All cores · Physical cores only · 4 cores · 1 core | **All cores** |

**Field of view** is not one number. The base setting substitutes the argument to
`CCameraComponent`'s `fFOV` property setter, but a wider view alone breaks things around it, so
fifteen further sites exist only to keep it honest: the weapon and arms draw in a nearer pass with
their own projection, ladders and cutscenes frame shots a wide view spoils, the hang glider's own
angle wins, muzzle particles are split between the two passes and come apart once the two
projections differ, and the map's markers change pass inside a vehicle. Sights keep their own value
unless the ironsight row is moved, and magnified optics are skipped entirely so scopes keep their
zoom.

**The two sensitivity rows** scale the value the game already loaded, so the in-game slider is left
alone and either device can go past its ceiling. Which one applies follows what the player is
actually looking with: pushing the right stick past the engine's dead zone claims the look, and any
mouse or keyboard input hands it back.

**Aim and sprint toggle** turn a tap into a latch by suppressing the button-up the engine would act
on. A press longer than a quarter second still behaves exactly as it always did, tapping sprint
while standing still cannot arm a run that never starts, and a latch is dropped when the action
leaves the map — which is what brings the sights down on entering a vehicle.

**Maximum frame rate** uses the engine's own limiter, `gfx_MaxFps`. It is set on the command line at
launch and written into the render profile afterwards, which is what lets a change made from the
menu take effect without a restart.

**Display mode** asks the engine for `-borderless`, a flag it already understands, so it applies on
the next launch. Fullscreen and windowed are on the game's own video page already.

**Full turn rate while sprinting** is a preference rather than a fix: the yaw slowdown is an
authored value the console builds have too.

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

## Credits and licences

Most of the input, field-of-view, startup and utilisation work here is ported from
[FC2JackalFix](../../tools/third-party/FC2JackalFix) by Joshhhuaaa and TGP482, which found every one
of those sites and documented them unusually well. Each ported file names the module it came from.
FC2JackalFix's own field-of-view work credits FoxAhead's
[Far Cry 2 Multi Fixer](https://github.com/FoxAhead/Far-Cry-2-Multi-Fixer) for the original `fFOV`
substitution, as does `src/options/fov.cpp` here.

Those files are covered by FC2JackalFix's licence, reproduced in full:

```
MIT License

Copyright (c) 2026 Joshhhuaaa, TGP482

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The Jackal tapes, bonus content, exit teardown and processor affinity work is this repository's own.

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
sources, by `scripts/verify_patterns.py`: exactly one match on at least one build, never two on any.

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

*(automated — local, needs the game)* `python ..\..\scripts\verify_patterns.py` re-checks every byte pattern
against both shipped `Dunia.dll` builds. It cannot run in CI, because that would mean putting a copy
of the game in the repository. Run it after touching a pattern.

Everything below needs a real install:

- `bin\fcse.log` shows `UFCP loaded`, then `jackal tapes fixed`, `predecessor tapes unlocked`,
  `machetes unlocked`, the exit guard installing, the FOV hook's address, and the affinity mask.
- **Exit crash** *(run, works)*: quitting through Exit Game ends with `RunGame returned true` and no
  `CRASH:` line — verified from the menu.
- `bin\fcse.ini` gains a `[UFCP]` group with `Field of view = 75` and
  `Processor affinity = All cores`.
- Moving the FOV slider logs the new value; setting it back to 75 logs that the game's own value is
  no longer being overridden.
- **In-game, none of which has been run yet**: tapes in the southern map advance instead of
  repeating; the predecessor-mission envelope appears in the central town; Options → Game offers
  Machete Type with three entries; a changed FOV takes effect after a load.
