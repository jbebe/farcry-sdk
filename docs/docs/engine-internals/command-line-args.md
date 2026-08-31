---
sidebar_position: 4
---

# `Dunia.dll` — Command-Line Parsing and Full Flag List

:::info[Verified via reverse engineering]
See [the overview](./overview.md) for binary identification.
:::

:::info[Verified in a running game]
Every entry in the [live-test results](#live-test-results) below was checked against the retail
`FarCry2.exe` on 2026-08-31, not inferred from the disassembly alone. Where a live run contradicted
what the code read like, the live result wins and the page says so.
:::

Traced from `RunGame` (`0x10006510`) down through `InitDuniaEngine` (`0x10004900`) and its callees,
fully decompiled and read. `FarCry2.exe` itself is a ~28 KB shim: it imports
`?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z` from `Dunia.dll` and hands the command line straight through,
so every flag on this page applies to `FarCry2.exe`.

Three independent parsing mechanisms exist:

1. **Ad-hoc `strstr(cmdline, "-flagname")` checks** scattered across several functions — presence-only
   flags, or (via the helpers `FUN_1003f7f0(cmdline, "-flagname", &outBuf)` and
   `FUN_104d0420(cmdline, "-flagname", ...)`) flags that also capture the following token as a value.
2. **A generic `-key value` tokenizer**, `ParseGamerProfileArgs` (`0x10661950`) — walks the whole
   command line splitting on `-`/space and stuffing every pair into a `"GamerProfile"` XML node via
   `FUN_106616e0(profile, key, value)`. The dash is stripped, so consumers look up bare names. Only
   pairs where an actual value follows are stored; presence-only flags never enter this bag.
3. **`CCryEngine::Initialize` (`0x104d0510`)** reads its own cluster of engine flags directly off the
   command line, separately from the two above.

## Matching is `strstr` against the raw command line

:::warning[Prefix collisions are real]
Every check is a substring match on the *whole* command line, not tokenised argument parsing. So
`-loadx` takes the `-load` branch, `-benchmarkloop` alone is enough to enter the `-benchmark` branch,
and `-spawn` matches inside `-spawnpos` and `-spawnangle`. Confirmed live: `-loadx` enters the
`-load` branch and dies there exactly as a `-load` naming a missing save does.
:::

## Dispatch is a first-match-wins chain

`CFCXGameCmdLineParser::Process` (`0x10663d40`) is a strict if/else-if ladder. The first match wins
and **every branch below it is dead for that launch**:

```
Process(cmdline)
  always: -exec, -notracking
  -editorpc   -> FUN_10661b40                     (editor-PC node)   RETURN
  -benchmark  -> CreateBenchmarkNode  [0x10662f90]                   RETURN
  -host       -> DispatchNetworkMode  [0x10663af0] -> CreateHostNode
  -join       -> DispatchNetworkMode              -> CreateClientNode
  -load       -> FUN_10661f50                                        (see below)
  -wait       -> DispatchNetworkMode              -> CreateHostNode
  (else)      -> CreateMainMenuNode   [0x106622f0]                   (-ubidays read here)
```

Note that `-client` is **not** a branch in this chain. It is only read *inside*
`DispatchNetworkMode`, which you can only reach via `-host`, `-join` or `-wait`, and in
`InitDuniaEngine` to build a window-title suffix.

## Which flags are required together

| Goal | Required combination |
|---|---|
| Load a world | `-benchmark <mode>` **and** (`-world <name>` or `-map <name>`) — neither works alone |
| `-benchmark` | must be accompanied by `-world` or `-map`, else a hard usage error |
| `-spawn` / `-spawnpos` / `-spawnangle` | only parsed inside the `-benchmark` branch |
| `-host` | requires `-login`, else a hard usage error |
| `-join` | requires nothing else — there is no `-login` gate on the client path |
| Headless | `-norender` only takes the headless path together with `-dedicated` |
| `-online` | incompatible with `-noagora` — the combination is a hard error |

:::note[Corrects an earlier claim on this page]
This page previously stated that `-world <name>` / `-map <name>` "load directly into a level,
skipping the main menu". **That is false on their own.** `-world`/`-map` are parsed only inside
`CreateBenchmarkNode`, which `Process` only reaches when `-benchmark` is present. Live-tested:
`FarCry2.exe -world world1` boots to the ordinary main menu with a 246 MB working set, identical to
a no-argument launch (248 MB).

It also stated that `-join` "hard-aborts if `-login` is absent". **Also false.** The `-login` gate
lives in `CreateHostNode`; `-join` routes to `CreateClientNode`, which has no such gate.
Live-tested: `-join 127.0.0.1` boots normally, with and without `-login`.

Finally, the `-benchmark` usage error was described as being triggered by an invalid sub-mode value.
The actual gate is the **absence of `-world`/`-map`** — the sub-mode string is only compared against
`"playback"` to decide whether `-benchmarkinputname` may substitute for it.
:::

## Window / bootstrap flags (checked directly in `InitDuniaEngine`, before engine subsystems init)

| Flag | Effect |
|---|---|
| `-borderless` | Borderless window — strips `WS_CAPTION`, leaving `WS_POPUP` |
| `-dedicated` | Dedicated-server mode; combined with `-norender` skips window/render init entirely |
| `-norender` | No rendering (only takes the headless branch together with `-dedicated`) |
| `-editorpc` | Remote-editor/PC connection mode — routes into `Process`'s editorpc branch (`FUN_10661b40`, not further explored) |
| `-xpos <n>` / `-ypos <n>` | Window position (only read when not `-host`/`-client`) |
| `-host` / `-client` | Presence checked here to build a window-title suffix; real network handling happens later in `DispatchNetworkMode` |
| `-d3dmts` | Sets a D3D multithread-safety flag (`DAT_10f92043 = 1`) |
| `-3dplatform <d3d10a\|d3d10\|d3d9>` | Forces the render backend. The three accepted value strings sit next to the flag at `0x10e09d1c`–`0x10e09d3c` |

## Engine flags (`CCryEngine::Initialize`, `0x104d0510`)

:::note[Not previously documented]
This whole cluster was missing from earlier versions of this page. The strings live together at
`0x10e6a334`–`0x10e6a3d0` and are read via `FUN_104d0420`.
:::

| Flag | Effect |
|---|---|
| `-nosound` | Disables the sound system (`GetSoundSystem()->vtbl+0x14(0)`) |
| `-nosndocc` | Disables sound occlusion (`GetSoundSystem()->vtbl+0x2c(1)`) |
| `-novoicechat` | Skips voice-chat init |
| `-16bitbroadphase` | Physics: 16-bit broadphase |
| `-nospuheightfield` | Physics: disables the SPU heightfield path (PS3-era naming, still parsed on PC) |
| `-norigidchars` | Physics: disables rigid-body characters |
| `-nomovecache` | Disables a movement cache |

## Config toggles (`ParseGameConfigFlags`, `0x10662c70` — runs first, at `InitDuniaEngine+0x74`)

| Flag | Effect |
|---|---|
| `-cmdfile <path>` | Intended to load additional arguments from a file (`FUN_10661cf0`) — **non-functional in retail, see below** |
| `-logFile <path>` | Parsed but non-functional in retail — see below |
| `-nomouse` | Disable mouse |
| `-noexmouse` | Disable "extended" mouse (raw input?) |
| `-nopad` | Disable gamepad |
| `-nobf` | Boolean toggle, name unexplored |
| `-nocompile` | Disable shader (or script) compilation |
| `-norender` | Also read here (duplicate of the bootstrap check above) |
| `-runscriptindebug` | Run Lua scripts in debug mode |
| `-zombieai` | Boolean AI toggle, name unexplored |
| `-usearchivecache` / `-noarchivecache` | Force-enable/disable the packed-archive read cache |

## World / spawn (`CreateBenchmarkNode`, `0x10662f90` — reachable only via `-benchmark`)

| Flag | Effect |
|---|---|
| `-world <name>` / `-map <name>` | Selects the level to load. `world1` is the campaign world |
| `-spawn <name>` | Spawn at a named spawn point |
| `-spawnpos <x,y,z>` | Spawn at an explicit position |
| `-spawnangle <yaw,pitch,roll>` | Spawn orientation, degrees (multiplied by `0.017453292`) |
| `-bfname <name>` | Session/"battlefield" display name (also read in the online-session parser) |

:::warning[The shipped Z values put the camera underground]
`FC2BenchmarkTool.exe` embeds its demo definitions as `benchmarkdemos.xml`, e.g.
`world="world1" spawnPos="1876.16,3523.59,34.81" spawnAngles="-13.0094,0.0,-172.572"`. Used with
`-benchmark record`, that exact Z spawns you **inside the terrain**. Raising Z by 2 clears it. So Z
is a usable world altitude, just not a safe one at the shipped values.
:::

## Save loading — `-load`

| Flag | Effect |
|---|---|
| `-load <savename>.sav` | Loads a save directly, skipping the menus. **Crashes as shipped**, for the reason below; UFCP (`mods/UFCP`) fixes it, after which this is the one working way to boot straight into playable gameplay |

:::note[Corrects an earlier claim on this page]
This page previously said `-load` "kills the game instantly, for every value", that the value was
therefore irrelevant, and that the blocking save load never worked. All three were wrong, and the
error was methodological: **every** failure here exits in about 0.3 s with exit code 0, so exit
timing cannot tell them apart. Running under FCSE, whose crash handler names the faulting address,
separates them immediately:

| Command | Fault | Meaning |
|---|---|---|
| `-load <name>` | `0x106621B8` | file not found |
| `-load <name>.sav` | `0x104DBB80` | **file found, opened and parsed** — dies afterwards |

The extension is required and is not a bug: `FUN_102a18f0` is a plain `basename`, and the parser
appends nothing, so a name without `.sav` matches no file.
:::

`GameFileUtils::GenerateRelativeFileName(name, mode)` (`0x101e9b20`) builds the path. The mode
switch is `0 → "Saved Games\"`, `1 → "Benchmarks\Playbacks\"`, `2 → "user_maps\"`,
`3 → "user_maps\downloads\"`; `-load` passes mode `0`, so the target is
`My Games\Far Cry 2\Saved Games\<name>`.

Unlike every other branch in `Process` — which merely *record intent* into a node object — the
`-load` branch calls `FUN_101cc960`, which spins up a **`GameFileBlockingLoadThread`**. That thread
(`FUN_101cd100`) opens and parses the save successfully. It then runs a post-load pass through its
context vtable slot `+0x14` (`0x1072EE60`) that binds the save's records to engine registries by
name, and *that* is what faults — on registries which do not exist yet:

```
1072ee8a  call 104DBB80h   ; ecx = [11644D74h], the settings manager - null
1072eeb3  call 10172820h   ; walks a registry at this+50h            - null
```

Inside `InitDuniaEngine` the call order is:

| Offset | Call |
|---|---|
| `+0x074` | `ParseGameConfigFlags` |
| **`+0x52C`** | **`Process` — the `-load` branch, including the blocking load, runs here** |
| `+0x10CF` | `CCryEngine::Initialize` — builds `0x11644D74` and the rest of the engine |

So the save is read correctly and then bound against an engine that has not been constructed.
`RunGame` turns the resulting fault into `return false`, which is the clean exit-code-0 observed.

UFCP skips that pass while the engine is absent, returning the pass's own "resolved cleanly" result
(`1`; `5` is its failure code). Live-confirmed: with the fix, `-load <name>.sav` boots directly into
the save, playable, at ~840 MB working set.

A name that matches **no** file still faults, at `0x106621B8`, and the fix above does not change
that: on a failed load the thread returns its error code `10` and `FUN_10661f50` reads the resulting
document pointer without checking it. That path is a separate bug from the one UFCP patches, and it
is only reachable by naming a save that is not there.

## Benchmark harness (`CreateBenchmarkNode`, entered whenever `-benchmark` is present)

| Flag | Effect |
|---|---|
| `-benchmark <playback\|record\|sectors\|spawnpoints\|path>` | Selects benchmark sub-mode. Requires `-world`/`-map`; without it: `"Invalid parameters for -benchmark. Ex; -benchmark {playback\|record\|sectors\|spawnpoints\|path} -world worldname -map mapname"` |
| `-benchmarkinputname <name>` | Names a playback file under `Benchmarks\Playbacks\`; accepted instead of `-benchmark playback` |
| `-benchmarkloop <n>` | Loop count for the benchmark (default 1) |
| `-benchmarkfixedframerate` | Force a fixed frame rate during the benchmark |
| `-benchmarkdisableai` | Disable AI during the benchmark (also pokes an engine flags int, `+0x20 = 0xfff7`) |
| `-benchmarkid <id>` | Numeric benchmark ID |

The shipped `FC2BenchmarkTool.exe` composes exactly these, e.g.
`-benchmark playback -world <w> -benchmarkinputname "<name>" -benchmarkloop <n> -benchmarkdisableai`.

Observed sub-mode behaviour:

- **`sectors`** — loads the world and sweeps a moving camera through it. Working set ~851 MB versus
  ~250 MB at the main menu.
- **`record`** — loads the world at the requested spawn and holds a static view. **No player
  control**: WASD and mouse-look are both dead, though <kbd>Esc</kbd> still quits. No player
  character is possessed; this is a camera harness, not gameplay.
- **`path`** — needs a playback file under `Benchmarks\Playbacks\`. With none present, it reaches a
  loading screen and then exits.

## Networking (`DispatchNetworkMode` `0x10663af0` → `CreateHostNode` `0x10662440` / `CreateClientNode` `0x10662b40`)

`CreateHostNode` aborts if `-login` is absent (`"Invalid parameters for -host, missing (-login)"`).
`CreateClientNode`, which is what `-join` reaches, does not.

| Flag | Effect |
|---|---|
| `-host` | Launch as server (**requires `-login`**) |
| `-client` | Read only inside `DispatchNetworkMode`; unreachable unless `-host`/`-join`/`-wait` is also present |
| `-join <ip>` | Connect to a server IP; validated with `inet_addr`. Usage strings: `"Trying to launch an online client with an invalid ip (-join xxx.xx.xx.xx)"` / `"...but ip is missing..."` |
| `-wait` | Wait-for-connection mode; routes to `CreateHostNode`, so it inherits the `-login` requirement |
| `-login <name>` | Ubi.com/Agora login name |
| `-password <pw>` | Login password |
| `-keyonline <key>` | CD-key/license token for online auth |
| `-sessionuid <uid>` | Numeric session ID (parsed with `_strtoui64`) |
| `-team` / `-ctf` / `-vip` | Selects game mode constant |
| `-online` | Agora-backed online, network scope code `2` |
| `-lan` | Sets scope code `3` — **which is already the default**, so this flag is a no-op |
| `-noagora` | Forces non-Agora networking; with `-online` it is a hard error |
| `-ranked` | Marks the session ranked |
| `-dedicated` | Also read here, marking the session dedicated for the online layer specifically |

## Misc

| Flag | Effect |
|---|---|
| `-exec <file>` | Execute a console/Lua command file at boot (read in `Process` before branching) |
| `-notracking` | Disables the telemetry/tracking client (read in `Process` before branching) |
| `-ubidays` | Requests a `"ubidays"` UI mode in `CreateMainMenuNode` (`0x106622f0`) — a trade-show/kiosk build hook. No visible effect in retail |
| `-openautomate` | QA automation path, below |

## QA automation path (`-openautomate`, handled entirely separately by `FUN_10005fa0`)

If `-openautomate` is present, `RunGame` skips the normal game loop entirely and enters a
numeric-command dispatch loop (`FUN_10299a00` returns a case 0–6, dispatching to
`FUN_10006710`/`FUN_10008620`/`FUN_10007050`/`FUN_100075a0`/`FUN_100065b0`/`FUN_10006600`) — an
internal QA/automation harness, not reachable through normal `-flag` parsing. None of these 6 handlers
have been examined.

## `-cmdfile` appears dead in the retail build

Live-tested with a file containing `-nomouse`: `FarCry2.exe -nomouse` disables the mouse, but
`FarCry2.exe -cmdfile <that file>` does not. The file's contents are never applied.

Note that this test has to use a flag consumed *through the config object* `ParseGameConfigFlags`
builds. Testing `-cmdfile` with `-borderless` or `-xpos` proves nothing either way, because those are
read directly off the raw command-line string in `InitDuniaEngine` and could never be injected by a
file regardless of whether `-cmdfile` works.

## `-logFile` appears dead in the retail build

Live-tested (`.\FarCry2.exe -logFile C:\path\engine.log`): **no file is created.** Traced why in
`ParseGameConfigFlags` and its caller `InitDuniaEngine`:

- The flag is genuinely parsed — `FUN_1003f7f0(cmdline, "-logFile", param_1 + 0x13)` captures the path
  into a dedicated `std::string` field of the config object `ParseGameConfigFlags` constructs (called
  twice, redundantly — a harmless duplicate). That config object is constructed directly on
  `InitDuniaEngine`'s own stack frame (confirmed via disassembly — the three boolean fields immediately
  after it, `nomouse`/`noexmouse`/`nopad`, are read back at fixed stack offsets and drive
  `DAT_10fd42c0..c2`, so the frame layout is confirmed, not guessed). The logfile string sits at
  `this+0x4c` in that frame.
- Full disassembly of `InitDuniaEngine` was read end to end looking for any read of that `this+0x4c`
  stack slot after the parse — none exists. No `CreateFileA`/`fopen`/log-write call anywhere in the
  function takes that buffer as an argument.
- The RTTI-derived class list does contain a `CLog`, but its mangled RTTI name is
  `.?AVCLog@MassiveAdClient3@@` — it belongs to the third-party **MassiveAdClient3** in-game-advertising
  SDK linked into this DLL, not an engine logging facility. No other `Log`-named class or function
  exists in the binary.
- Consistent with the independently-sourced community finding that [Far Cry 2 retail has no in-game
  dev console](../modding/gotchas.md) — debug/dev-facing instrumentation reads as compiled-out or
  stubbed for the shipped build, not merely hidden behind a flag.

**Conclusion**: `-logFile`'s value is captured and then goes nowhere within the boot path — vestigial
parsing left over from a development build whose actual log sink was stripped for retail, not a flag
the user is invoking wrong. Not proven for the entire 20MB DLL (this traced one function's disassembly
exhaustively, not every one of the ~90k functions in the binary), but no plausible consumer turned up
anywhere reachable from boot.

## Live-test results

Method: launch retail `FarCry2.exe`, sample process liveness/exit code, main-window title, window
style and rect, and working-set size; kill and repeat. Visual and audible outcomes were confirmed by
a human watching each run.

Useful discriminators: main menu sits at ~170–250 MB working set, a loaded world at ~750–850 MB;
usage errors surface as a real window titled `Error`; `-borderless` shows up as window style
`0x94000000` instead of the default `0x14CA0000`.

| Flag / combination | Result |
|---|---|
| *(no arguments)* | Baseline: title `Far Cry 2`, style `0x14CA0000`, rect `0,0 1286x749`, 248 MB |
| `-borderless` | **Works.** Style → `0x94000000` (`WS_POPUP`), client size stays 1280x720 |
| `-xpos 100 -ypos 200` | **Works.** Origin → `100,200`. Composes with `-borderless` |
| `-nosound` | **Works.** Completely silent |
| `-nomouse` | **Works.** Mouse dead in the menu |
| `-spawnpos` / `-spawnangle` | **Work.** Two different shipped spawn values start in visibly different places |
| `-benchmark sectors -world world1` | **Works.** 851 MB, world loaded, camera sweeping |
| `-benchmark record -world world1 -spawnpos …` | **Loads the world**, but no player control (camera harness only) |
| `-dedicated -norender` | **Works.** No window, clean exit after 4.3 s |
| `-benchmark playback` *(no `-world`)* | **Usage error** — `Error` window, 401x156 |
| `-host` *(no `-login`)* | **Usage error** — `Error` window, 275x130 |
| `-join 127.0.0.1` | Boots to the normal main menu. Same with `-login` added — no gate |
| `-editorpc` | Boots to the normal main menu; no observable effect |
| `-3dplatform d3d10` | Boots normally; which backend actually loaded was **not** confirmed |
| `-world world1` *(alone)* | **Ignored.** Normal main menu, 246 MB |
| `-ubidays` | **No visible effect** |
| `-load <name>.sav` | **Works with UFCP.** Boots into the save at ~840 MB. Unpatched it faults at `0x104DBB80` |
| `-load <name>` *(no extension)* | File not found — faults at `0x106621B8` |
| `-cmdfile <file>` | **Broken.** File contents never applied |
| `-zzznotaflag` | Unknown flags are harmless — boots normally |

`-3dplatform` could not be verified: a 64-bit host cannot enumerate a WOW64 process's loaded modules
(`tasklist /m` returns only the WOW64 shim), so there was no way to observe which D3D DLL loaded.

## Flags this page does not cover behaviourally

These are parsed for certain but have no signal observable from outside the process, and were not
individually verified: `-noexmouse`, `-nopad`, `-nobf`, `-nocompile`, `-runscriptindebug`,
`-zombieai`, `-usearchivecache`, `-noarchivecache`, `-d3dmts`, `-nosndocc`, `-novoicechat`,
`-nomovecache`, `-norigidchars`, `-nospuheightfield`, `-16bitbroadphase`, the `-benchmark*`
sub-flags, and the multiplayer flags beyond the `-login` gate.

## Flags that exist only in `FC2ServerLauncher.exe`

Not present in `Dunia.dll`:

| Flag | Effect |
|---|---|
| `-noredirectstdin` | Documented in `FC2ServerLauncher_ReadMe.txt` (added in Dedicated Server 1.03 R2): lets an external app feed the server console via standard input |
| `-help` | Present as a string; behaviour not examined |

## Enumerating the flags yourself

The complete set is 58 dash-prefixed strings in `Dunia.dll`, in three clusters:
`0x10e09ac8`–`0x10e09d94` (bootstrap/window), `0x10e6a334`–`0x10e6a3d0` (engine), and
`0x10e8d190`–`0x10e8d524` (`CFCXGameCmdLineParser`), plus `-noagora` at `0x10e41334`.

:::warning[A string search under-reports the flag list]
Ghidra's string index misses six real flags — `-map`, `-lan`, `-vip`, `-ctf`, `-3dplatform` and
`-16bitbroadphase` — because they sit in padding gaps between indexed strings or begin with a digit.
They are only visible by dumping those three address ranges as raw bytes and reading the
null-terminated runs directly. Any future re-enumeration should dump the ranges, not grep the string
table.
:::

## Unknowns

- The exact semantics of `-nobf` and `-zombieai` — booleans read but never named beyond their flag
  string.
- The `-editorpc` handler `FUN_10661b40` itself, which shows no observable effect in retail.
- Whether `-3dplatform` actually switches backend on a retail install.
- The six `-openautomate` sub-handlers.
