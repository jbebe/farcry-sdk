# DevTools

The developer side of Far Cry 2, made reachable.

DevTools is an [FCSE](../../tools/FCSE) plugin for people working on the game rather than playing
it. Like [UFCP](../UFCP) it patches `Dunia.dll` in memory at startup, so it overwrites nothing,
leaves no trace when uninstalled, and stacks with whatever else is installed. The two are separate
plugins because they are for different people: UFCP is what a player installs, this is what a modder
or a reverse engineer installs, and either can be dropped into `bin\plugins\` without the other.

## Fixes

Applied unconditionally, with no setting.

- **Savegame launch** — `-load <name>.sav` quit to desktop in a fraction of a second instead of
  booting straight into the save. The command line's own way to skip the menus, unusable as shipped,
  and the difference between testing a change in seconds and clicking through them every time.

## Options

One row each in the Mod Configuration Menu, saved under `[DevTools]` in `bin\fcse.ini`. **Every one
defaults to leaving the game exactly as it shipped**, so installing DevTools applies its fix and
changes nothing else.

**Developer console**, off by default, lifts the `ConsoleDeveloperOnly` filter from Far Cry 2's own
console, which opens on `~` whether DevTools is installed or not. About 57 commands — `load_level`, `set_health`,
`teleport_to_current_objective`, `aidebugtool`, `console_dump_elements` and the rest — are hidden
from the `?` listing and from command lookup alike, so typing one answers "Unknown command"; with
this on they are listed and they run. Only the developer test is lifted: the context mask that keeps
multiplayer-only and editor-only commands out of a single-player console still applies. Note that
none of this is needed to *script* the game — a `#` prefix already runs arbitrary Lua past every
filter — so this is about making the built-in commands reachable by name. See
[the developer console](../../docs/docs/engine-internals/developer-console.md).

## Command API

Far Cry 2's console, callable from code. `src/engine/console.h` runs a line exactly as typing it
would — `#` Lua escape included — and raises the developer flag for the duration, so a
developer-only command runs whether or not the option above is on. `src/engine/game_thread.h` is
the frame the work runs on: a line posted from anywhere is copied and executed at the end of the
next one, on the thread the engine updates from.

`src/commands/catalog.h` is the same thing as data. 277 commands, each with a category, what kind of
argument it takes and what to call it, so whatever drives them enumerates the table instead of
hard-coding names. 89 are described by hand, down to the values a cheat or a draw method accepts;
the other 188 are Far Cry 2's own config settings, listed straight out of
`ConsoleElementsDump.txt` and uniform enough to need nothing said about them.

Only commands that still do something are in it. The engine registers 416 names, and a good number
reach a handler that reads its arguments and returns, or set a value nothing looks at —
`load_level`, `aidebugtool`, `runtests`, `SetMaxFrameRate` and the whole `set_weather` family among
them. Which ones, and how each verdict was reached, is in
[the developer console](../../docs/docs/engine-internals/developer-console.md).

**Nothing here is a setting.** No row in the Mod Configuration Menu, no key in `bin\fcse.ini`, and
nothing calls into the catalog yet: this is the half that has to exist before an on-screen overlay
can be written against it.

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

## How a feature finds the code it patches

Every site here is found by `FCSE::Relocation{FCSE::Pattern(...)}`, matched on the bytes about to be
replaced, rather than by the address library — which is keyed by exact entries, function starts and
data addresses, while each of these sites is *inside* a function, one branch displacement halfway
down a loop. A pattern is one mechanism doing three jobs: it finds the code, it verifies the code is
what the feature was written against, and it works on any build whose code still looks the same
rather than only the two that are mapped. FCSE reports a pattern that matched in more than one place
as no match at all, so a feature either lands on its one site or logs that it could not.

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
.NET SDK, unlike FCSE: DevTools embeds no `.mgb` layouts, so JackAll is not in its build.

`.\verify_build.ps1 [-Config debug]` checks the three properties of a built `DevTools.dll` that fail
*silently* — x86, static CRT, and the `FCSE_Load` export. All three produce a plugin that is simply
never there, with FCSE itself starting up perfectly. Both it and the build run in the release
configuration on every push and pull request touching `mods/DevTools` or the plugin ABI header
(`.github/workflows/devtools-ci.yml`), and again before a release is packaged
(`.github/workflows/devtools-release.yml`, dispatched with a version, producing
`devtools-{version}.zip`).

There is no test suite, deliberately. DevTools is byte patches and hooks against a live `Dunia.dll`;
it holds no pure logic worth a suite, and stubbing the engine to manufacture some would test the
stub.

## Installing

1. Install FCSE (`FCSE.exe` next to the game's `FarCry2.exe`).
2. Drop `DevTools.dll` into `bin\plugins\`.
3. Launch `FCSE.exe`.

## Verification

*(automated — CI)* The release build and `verify_build.ps1`.

*(automated — local, needs the game)* `python ..\..\scripts\verify_patterns.py` re-checks every byte pattern
against both shipped `Dunia.dll` builds. It cannot run in CI, because that would mean putting a copy
of the game in the repository. Run it after touching a pattern.

Everything below needs a real install:

- `bin\fcse.log` shows `DevTools loaded`, then `-load: savegame launches skip the post-load pass
  until the engine exists`.
- **Savegame launch** *(run, works)*: `FCSE.exe -load <name>.sav` boots straight into that save.
- `bin\fcse.ini` gains a `[DevTools]` group with `Developer console = false`.
- Toggling Developer console logs `developer console: on` or `off`; a build where a gate could not
  be found says so instead of failing quietly.
- **In-game, not yet run against this plugin**: with Developer console on, `~` then `?` lists
  `load_level` and friends, and `console_dump_elements` writes `ConsoleElementsDump.txt`.
- **Command API** *(run, works — GOG v1.03, 2026-09-05)*: `bin\fcse.log` shows `game thread: hooked
  the frame update at 0x104C2510` and `console: command API ready`. That address is the GOG one,
  translated by the address library from the Steam address the source names, so this is also the
  build-agnostic path confirmed on a build nobody opened. With nothing calling the catalog yet,
  exercising it takes a temporary job posted from `FCSE_Load`, with `Developer console = false`:

  ```cpp
  DevTools::GameThread::Post([] {
      DevTools::Console::Print("DevTools: command API online");
      DevTools::Console::Execute("console_dump_elements");
  });
  ```

  `ConsoleElementsDump.txt` was rewritten with the option **off**, at a timestamp matching the
  `console: > console_dump_elements` log line to the second. That command is developer-gated, so
  that one file proves the whole path: the string laid out as the engine's own, the call into
  `CXConsole::ExecuteString`, the queue draining on the game thread, and the developer flag the API
  raises for itself rather than relying on the option.
