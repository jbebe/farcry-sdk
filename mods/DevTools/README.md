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

One row each in the Mod Configuration Menu, saved under `[DevTools]` in `bin\fcse.ini`. FCSE owns
the stored value, so whatever you set survives a relaunch.

**Four are on out of the box** — the developer console, the FPS readout, the startup skip and the
free camera on `F2` — because they are what the plugin is installed for. **Everything that changes
how the game plays is off**: god mode, infinite ammo, unlocked weapons and diamonds. So installing
DevTools opens the game up without altering a playthrough until you ask it to.

**Developer console**, off by default, lifts the `ConsoleDeveloperOnly` filter from Far Cry 2's own
console, which opens on `~` whether DevTools is installed or not. About 57 commands — `load_level`, `set_health`,
`teleport_to_current_objective`, `aidebugtool`, `console_dump_elements` and the rest — are hidden
from the `?` listing and from command lookup alike, so typing one answers "Unknown command"; with
this on they are listed and they run. Only the developer test is lifted: the context mask that keeps
multiplayer-only and editor-only commands out of a single-player console still applies. Note that
none of this is needed to *script* the game — a `#` prefix already runs arbitrary Lua past every
filter — so this is about making the built-in commands reachable by name. See
[the developer console](../../docs/docs/engine-internals/developer-console.md).

**Invincibility**, off by default, is three things rather than one. The `cheat_GodMode` profile flag,
re-applied every frame because a profile reload puts the file's value back. A health floor, because
that flag stops damage but never lifts a player who is already hurt back above the health-failure
threshold — the floor tops the counter back up through the engine's own `SetToMax`, and leaves alone
the three states that park the player below the threshold on purpose, so a scripted failure and a
buddy rescue still play out. And vehicles, which the flag reaches not at all: both damage entry
points on the physics component are hooked, for the vehicle the player is actually in. Drowning and
scripted destruction stay lethal.

**Infinite ammo**, off by default, sets the `cheat_UnlimitedAmmo` profile flag — and then puts the
syringe back. Magazines and consumables come out of one shared decrementer that returns early while
that flag is set, so unlimited ammo silently makes healing free and removes the malaria economy the
campaign is built on. The three call sites that spend a syringe are identified by their return
address and the flag is cleared around that one call. This corrects the `cheat_UnlimitedAmmo`
catalog row too, which reaches the same flag by a different road.

**Unlock all weapons**, off by default. The `cheat_AllWeaponsUnlock` flag bypasses the per-weapon
unlock list, which is why its catalog row can only promise the weapons the current map offers; the
two act-tag comparisons in `CWeaponBazaar::IsWeaponUnlocked` are what actually hold the rest back.
Both are patched while this is on, so the bazaar stocks every act.

**Diamonds**, `0` by default, is a target rather than a gift. `Cheat_AddDiamonds` adds a number and
that is the end of it — the diamonds are real, they go into the next save, and a test run funded that
way has rewritten the profile it ran on. This tops the wallet up to the setting every frame and
remembers the shortfall as a grant: spending comes off the grant first, lowering the setting hands
back only what is above it, and a save is written with the grant subtracted out. Set it to zero and
the grant is handed back.

**Noclip key** and **Freecam key**, both `Off` by default, are two ways to leave the ground, and only
one can be up at a time. Neither is reachable from the console — see
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md).

**FPS counter**, off by default, writes the global behind the `showFps` console variable directly.
The catalog row does the same thing, but does not survive a restart and cannot be used before the
console registers the variable; this is delivered at load, so the readout is up for the loading
screen too.

**Skip system detection**, off by default, cuts the hardware probe out of startup. `systemdetection.dll`
spins up WMI to enumerate the hardware and DxDiag to enumerate the display before the menu appears,
and both are worth seconds on every launch. WMI is refused outright and DxDiag is proxied and cached,
by redirecting one import in that one module. **This is the only option here that can crash on some
machines**, which is why it is in DevTools rather than UFCP: seconds off a hundred relaunches is
worth a risk no player should be asked to take.

The probe runs once and early, before the menu, so switching this on has no effect on the launch you
are in — it is saved and applies to the next one. The log says as much.

## Camera modes

Two ways to fly, bound from the Mod Configuration Menu and off until you bind them. Only one can be
up at a time, and picking a key the other mode already holds leaves the one you just set unbound —
one key toggling between both would leave no way back to the game. The log says when that happens.

**Noclip** detaches the player: physics off, so no collision and no gravity, and the body flown
directly. The view stays on the head, so it is still the player's camera and the HUD is still up,
which is what makes it useful for walking a level rather than photographing one. The pause menu,
quicksave and quickload are refused while it is up — saving a position the player could not have
reached ends badly.

**Freecam** leaves the player standing and detaches only the view, which is the one to use when the
thing you want to look at is the player, or a firefight that has to keep happening while you watch.
It activates a free camera the shipped game still carries and never uses. That camera is
entity-library data rather than code, so a heavily modded data set can simply not have it; if so the
log says as much.

| | |
|---|---|
| `W` `A` `S` `D` | move |
| `Space` / `Left Ctrl` | up / down |
| mouse | steer |
| arrow keys | steer, freecam only |
| `Left Shift` / `Left Alt` | cycle speed up / down, ten steps, wrapping back to normal |

Noclip's speed tops out lower than the free camera's, because the world still has to stream in
around a body that is really there.

## These and the `cheat_*` commands

Five of the options above overlap a row in the command catalog — `cheat_GodMode`,
`cheat_UnlimitedAmmo`, `cheat_AllWeaponsUnlock`, `Cheat_AddDiamonds` and `showFps`. The rows stay,
because they are the game's own commands and cost nothing to keep, but each option above does
strictly more than the row it overlaps, and the reasons are in each entry. If you want the shipped
behaviour, the row is still there.

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

**Nothing here is a setting.** No row in the Mod Configuration Menu and no key in `bin\fcse.ini`.

## Overlay

**Home** opens it, in game or in a menu. The command catalog on screen, split across a tab per
category — `All` first, then `Cheats`, `Player`, `Camera` and the rest — with an argument box or a
pair of buttons depending on what each command takes, and a note on hover for the ones with something
to warn about. There are more categories than fit across the window, so the tabs scroll and the small
button at their left end lists them all. Running a command queues it for the engine's next frame, so
the click and the command are never on the same thread as each other.

Other plugins can add windows of their own. Each time Home opens the overlay, the open windows are
laid out side by side — DevTools' first, then the rest in the order they were added — and after that
each can be dragged anywhere. A window's close button closes only that window, and it stays closed
until the game restarts: the bar across the top lists every closed window, and a click opens it
again.

The options above are not here: they live in the Mod Configuration Menu because that is what saves
them. FCSE owns a setting's stored value and offers no way to write one back, so a switch in the
overlay could change the running game but never the file.

While it is open the game cannot see your mouse or keyboard, so clicking a button does not also fire
your weapon. Press Home again, or close the last open window, and input goes straight back.

It draws with [Dear ImGui](https://github.com/ocornut/imgui), fetched at configure time and compiled
into the plugin. Reaching Direct3D takes no address in `Dunia.dll` at all: a device built purely to
be measured gives the vtable, `Present` and `Reset` are detoured through FCSE, and the back buffer is
bound explicitly for the draw. Far Cry 2 ends a scene several times a frame and holds the mouse
through DirectInput, and both of those shape the result — see
[presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md).

### Adding a window from another plugin

`include/devtools_api.h` is the contract, the way `fcse_api.h` is FCSE's, and says what DevTools does
with a window and what it refuses. A plugin adds one from its `FCSE_OnRegisterFunctions`, which runs
once every plugin has loaded:

```cpp
#include "devtools_api.h"

void DrawMyWindow(void*) { ImGui::TextUnformatted("Hello from my plugin"); }

extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api) {
    DevTools::Overlay::AddWindow(api, "My plugin", 360.0f, 200.0f, &DrawMyWindow);
}
```

It builds its own Dear ImGui by including `include/devtools_imgui.cmake` and linking `imgui`.
[Sky Overhaul](../sky-overhaul) is the working example.

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

Every site *in `Dunia.dll`* is found one of two ways. The fixes and options patch code inside a
function — a branch displacement halfway down a loop — so they use
`FCSE::Relocation{FCSE::Pattern(...)}`, matched on the bytes about to be replaced, rather than the
address library, which is keyed by exact entries: function starts and data addresses. The console
bridge is the other case, all function starts and globals, so it uses the library directly.

Direct3D and DirectInput are neither. Those functions live in `d3d9.dll` and `dinput8.dll`, shared by
every object in the process, so the overlay builds one object of its own, reads the vtable, and
hooks by index — no `Dunia.dll` address is involved and nothing about it is build-specific.

A pattern is one mechanism doing three jobs: it finds the code, it verifies the code is
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

Needs the same x86 MSVC toolchain as FCSE. Its whole dependency *on FCSE* is
`tools/FCSE/include/fcse_api.h`, the one header a third-party plugin would copy out of the tree, and
it never reaches into `tools/FCSE/src/`. No .NET SDK, unlike FCSE: DevTools embeds no `.mgb`
layouts, so JackAll is not in its build.

The overlay adds one third-party dependency, Dear ImGui, pinned at `v1.91.5` in
`include/devtools_imgui.cmake` and cloned by CMake at **configure** time. So the first configure needs `git` and a network connection; an offline machine
fails there rather than at compile. It is built from source into `DevTools.dll` — there is no second
file to install.

`.\verify_build.ps1 [-Config debug]` checks the properties of a built `DevTools.dll` that fail
*silently* — x86, static CRT, the `FCSE_Load` export, and the `DevTools_GetOverlayAPI` export other
plugins find the overlay by. Each leaves a plugin, or another plugin's window, simply never there,
with FCSE itself starting up perfectly. Both it and the build run in the release
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
- **Overlay windows** *(not yet run)*: with Sky Overhaul installed alongside, `bin\fcse.log` shows
  `overlay: added the 'DevTools' window` and later `overlay: added the 'Sky Overhaul' window`. Home
  lays the two out side by side under the bar, either can be dragged, closing one lists it in the bar
  and a click brings it back where it was, and closing the last gives the game its input back.
